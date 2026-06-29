using System;
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
    /// requests from stdin (one per line) and writes responses to stdout. It runs on the
    /// remote host where the repository actually lives, so git is launched as a local
    /// child process via <see cref="LocalCommandRunner"/> and files are accessed directly.
    /// <para>
    /// The server owns no GUI and no Avalonia lifetime; it blocks on stdin until the SSH
    /// channel closes, then exits.
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
                // Encoding may be unavailable in some environments; the loop still works
                // for ASCII content.
            }

            // The GUI path locates git via Preferences.PrepareGit(); the headless server
            // never starts that path, so locate git explicitly before handling exec_git.
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
                    // Keep serving even if a single message fails.
                    Native.OS.LogException(e);
                }
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
                // Malformed input — nothing to correlate a response with, ignore.
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

            // Serialize and flush on the loop thread so messages stay whole-line ordered.
            var json = JsonSerializer.Serialize(resp);
            Console.Out.WriteLine(json);
            Console.Out.Flush();
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

                case "file_exists":
                    return JsonValue.Create(File.Exists(GetString(p, "path")));

                case "dir_exists":
                    return JsonValue.Create(Directory.Exists(GetString(p, "path")));

                case "read_file":
                    return JsonSerializer.SerializeToNode(new { content = File.ReadAllText(GetString(p, "path")) });

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

                default:
                    throw new Exception($"unknown method: {req.Method}");
            }
        }

        private static async Task<ExecGitResult> ExecGitAsync(JsonNode p)
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

            // Launch git locally (the server host *is* the repo host). The runner injects
            // the same SSH askpass / GIT_SSH_COMMAND / locale env as the GUI client does.
            // Note: askpass currently needs a GUI; fetch/push over SSH from the server is
            // addressed in a later phase via an askpass callback over this same RPC channel.
            using var proc = LocalCommandRunner.Instance.Start(spec);

            // Read stdout/stderr concurrently to avoid pipe deadlocks on large output.
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
    }
}
