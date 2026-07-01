using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using SourceGit.Commands;

namespace SourceGit.Remote
{
    /// <summary>
    /// Headless RPC server started by <c>sourcegit --remote-server</c>. Reads JSON-RPC
    /// requests from stdin and writes responses to stdout, blocking until the SSH channel
    /// closes. It runs on the remote host where the repository lives, so git is launched as a
    /// local child process and files are accessed directly.
    /// <para>
    /// Also supports <c>watch_start</c>/<c>watch_stop</c>: a <see cref="FileSystemWatcher"/> is
    /// started on the remote working tree and change events are pushed back to the client as
    /// <c>watch_event</c> notifications so the client can auto-refresh a remote repository.
    /// </para>
    /// </summary>
    public class RpcServer
    {
        public int Run()
        {
            try
            {
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(Native.OS.GitExecutable) || !File.Exists(Native.OS.GitExecutable))
                Native.OS.GitExecutable = Native.OS.FindGitExecutable();

            while (Console.In.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    ProcessLineAsync(line).GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    Native.OS.LogException(e);
                }
            }

            lock (_watchers)
            {
                foreach (var kv in _watchers)
                {
                    try { kv.Value.EnableRaisingEvents = false; kv.Value.Dispose(); } catch { }
                }
                _watchers.Clear();
            }

            return 0;
        }

        private async Task ProcessLineAsync(string line)
        {
            Request req;
            try
            {
                req = JsonSerializer.Deserialize<Request>(line);
            }
            catch
            {
                return;
            }

            if (req == null || string.IsNullOrEmpty(req.Method))
                return;

            Response resp;
            try
            {
                var result = await DispatchAsync(req).ConfigureAwait(false);
                resp = new Response { Id = req.Id, Result = result };
            }
            catch (Exception e)
            {
                resp = new Response { Id = req.Id, Error = new RpcError { Code = -1, Message = e.Message } };
            }

            var json = JsonSerializer.Serialize(resp);
            lock (_sendLock)
            {
                Console.Out.WriteLine(json);
                Console.Out.Flush();
            }
        }

        private async Task<JsonNode> DispatchAsync(Request req)
        {
            var p = req.Params;

            switch (req.Method)
            {
                case "ping":
                    return JsonSerializer.SerializeToNode(new { pong = true });

                case "exec_git":
                    return JsonSerializer.SerializeToNode(await ExecGitAsync(p).ConfigureAwait(false));

                case "exec_git_stream":
                    return await ExecGitStreamAsync(p).ConfigureAwait(false);

                case "file_exists":
                    return JsonValue.Create(File.Exists(GetString(p, "path")));

                case "dir_exists":
                    return JsonValue.Create(Directory.Exists(GetString(p, "path")));

                case "read_file":
                    return JsonSerializer.SerializeToNode(new { content = File.ReadAllText(GetString(p, "path")) });

                case "read_file_base64":
                    return JsonSerializer.SerializeToNode(new { content = Convert.ToBase64String(File.ReadAllBytes(GetString(p, "path"))) });

                case "write_file":
                    File.WriteAllText(GetString(p, "path"), GetString(p, "content"));
                    return JsonSerializer.SerializeToNode(new { });

                case "delete_file":
                    File.Delete(GetString(p, "path"));
                    return JsonSerializer.SerializeToNode(new { });

                case "delete_dir":
                    Directory.Delete(GetString(p, "path"), GetBool(p, "recursive"));
                    return JsonSerializer.SerializeToNode(new { });

                case "get_temp_file":
                    {
                        var path = Path.GetTempFileName();
                        File.WriteAllText(path, GetString(p, "content"));
                        return JsonSerializer.SerializeToNode(new { path });
                    }

                case "home_dir":
                    return JsonSerializer.SerializeToNode(new { path = HomeDir() });

                case "list_dir":
                    return JsonSerializer.SerializeToNode(ListDir(TryGetString(p, "path")));

                case "watch_start":
                    StartWatch(GetString(p, "path"));
                    return JsonSerializer.SerializeToNode(new { });

                case "watch_stop":
                    StopWatch(GetString(p, "path"));
                    return JsonSerializer.SerializeToNode(new { });

                default:
                    throw new Exception($"unknown method: {req.Method}");
            }
        }

        private static async Task<ExecGitResult> ExecGitAsync(JsonNode p)
        {
            var (spec, stdin) = BuildGitSpec(p);

            using var proc = LocalCommandRunner.Instance.Start(spec);

            var stdoutTask = proc.Stdout.ReadToEndAsync();
            var stderrTask = proc.Stderr.ReadToEndAsync();

            if (stdin != null)
            {
                await proc.Stdin.WriteAsync(stdin).ConfigureAwait(false);
                proc.Stdin.Close();
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            return new ExecGitResult { Stdout = stdout, Stderr = stderr, ExitCode = proc.ExitCode };
        }

        /// <summary>
        /// Streaming variant of <c>exec_git</c>. Returns a stream id immediately, then pushes
        /// <c>exec_git_stream_data</c> notifications (base64 chunks of stdout) followed by
        /// <c>exec_git_stream_done</c> (exit code + stderr). This lets the client process large
        /// git log/diff outputs incrementally instead of buffering one giant JSON string.
        /// </summary>
        private async Task<JsonNode> ExecGitStreamAsync(JsonNode p)
        {
            var (spec, stdin) = BuildGitSpec(p);
            var streamId = Interlocked.Increment(ref _nextStreamId).ToString();

            _ = Task.Run(() => StreamGitAsync(streamId, spec, stdin));
            return JsonSerializer.SerializeToNode(new { stream_id = streamId });
        }

        private async Task StreamGitAsync(string streamId, Command.RunSpec spec, string stdin)
        {
            try
            {
                using var proc = LocalCommandRunner.Instance.Start(spec);

                if (stdin != null)
                {
                    proc.Stdin.Write(stdin);
                    proc.Stdin.Close();
                }

                var stderrTask = proc.Stderr.ReadToEndAsync();
                var buf = new byte[64 * 1024];
                int read;

                while ((read = await proc.StdoutStream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                    SendNotification("exec_git_stream_data", new { stream_id = streamId, data = Convert.ToBase64String(buf, 0, read) });

                var stderr = await stderrTask.ConfigureAwait(false);
                await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

                SendNotification("exec_git_stream_done", new { stream_id = streamId, exit_code = proc.ExitCode, stderr });
            }
            catch (Exception e)
            {
                SendNotification("exec_git_stream_done", new { stream_id = streamId, exit_code = -1, stderr = e.Message });
            }
        }

        private static (Command.RunSpec spec, string stdin) BuildGitSpec(JsonNode p)
        {
            var args = GetString(p, "args");
            var workingDir = GetString(p, "working_dir");
            var sshKey = TryGetString(p, "ssh_key");
            var editorStr = TryGetString(p, "editor");
            var stdin = TryGetString(p, "stdin");

            var editor = Command.EditorType.None;
            if (!string.IsNullOrEmpty(editorStr) &&
                Enum.TryParse(editorStr, ignoreCase: true, out Command.EditorType et))
            {
                editor = et;
            }

            var spec = new Command.RunSpec
            {
                Args = args,
                WorkingDirectory = workingDir,
                SSHKey = sshKey ?? string.Empty,
                Editor = editor,
                RedirectStandardInput = stdin != null,
                Headless = true,
            };

            return (spec, stdin);
        }

        private static string HomeDir()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? "/" : home.Replace('\\', '/');
        }

        /// <summary>
        /// List the sub-entries of a directory on the host so the client can browse for a
        /// repository path. Expands <c>~</c>/empty to the home directory, resolves to an
        /// absolute path and returns directories first. Entries that cannot be stat'd are
        /// skipped rather than aborting the whole listing.
        /// </summary>
        private static ListDirResult ListDir(string path)
        {
            var resolved = ResolvePath(path);
            var result = new ListDirResult { Path = resolved };

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(resolved))
                {
                    var name = Path.GetFileName(dir.TrimEnd('/'));
                    if (!string.IsNullOrEmpty(name))
                        result.Entries.Add(new ListDirEntry { Name = name, IsDir = true });
                }

                foreach (var file in Directory.EnumerateFiles(resolved))
                {
                    var name = Path.GetFileName(file);
                    if (!string.IsNullOrEmpty(name))
                        result.Entries.Add(new ListDirEntry { Name = name, IsDir = false });
                }
            }
            catch
            {
                // Return whatever we managed to collect (possibly empty) for an unreadable dir.
            }

            result.Entries.Sort((l, r) =>
            {
                if (l.IsDir != r.IsDir)
                    return l.IsDir ? -1 : 1;
                return string.Compare(l.Name, r.Name, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        private static string ResolvePath(string path)
        {
            var home = HomeDir();
            if (string.IsNullOrWhiteSpace(path) || path == "~" || path == ".")
                return home;

            if (path.StartsWith("~/", StringComparison.Ordinal))
                path = home.TrimEnd('/') + path.Substring(1);

            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                return path.Replace('\\', '/');
            }
        }

        private void StartWatch(string path)
        {
            lock (_watchers)
            {
                if (_watchers.ContainsKey(path))
                    return;

                FileSystemWatcher fsw;
                try
                {
                    fsw = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true,
                    };
                }
                catch
                {
                    return;
                }

                fsw.Changed += (_, e) => OnWatchEvent(path, e.Name);
                fsw.Created += (_, e) => OnWatchEvent(path, e.Name);
                fsw.Deleted += (_, e) => OnWatchEvent(path, e.Name);
                fsw.Renamed += (_, e) => OnWatchEvent(path, e.Name);

                _watchers[path] = fsw;
            }
        }

        private void StopWatch(string path)
        {
            lock (_watchers)
            {
                if (_watchers.TryGetValue(path, out var fsw))
                {
                    try { fsw.EnableRaisingEvents = false; fsw.Dispose(); } catch { }
                    _watchers.Remove(path);
                }
            }
        }

        private void OnWatchEvent(string path, string file)
        {
            SendNotification("watch_event", new { path, file });
        }

        private void SendNotification(string method, object parameters)
        {
            var notif = JsonSerializer.Serialize(new Notification
            {
                Method = method,
                Params = JsonSerializer.SerializeToNode(parameters),
            });

            lock (_sendLock)
            {
                try { Console.Out.WriteLine(notif); Console.Out.Flush(); } catch { }
            }
        }

        private static string GetString(JsonNode p, string name)
        {
            return p?[name]?.GetValue<string>() ?? throw new Exception($"missing param: {name}");
        }

        private static string TryGetString(JsonNode p, string name)
        {
            return p?[name]?.GetValue<string>();
        }

        private static bool GetBool(JsonNode p, string name)
        {
            return p?[name]?.GetValue<bool>() ?? throw new Exception($"missing param: {name}");
        }

        private readonly object _sendLock = new();
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
        private static long _nextStreamId = 0;
    }
}
