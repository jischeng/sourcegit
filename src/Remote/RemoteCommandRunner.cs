using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using SourceGit.Commands;

namespace SourceGit.Remote
{
    /// <summary>
    /// <see cref="ICommandRunner"/> that forwards git execution to a remote
    /// <c>sourcegit --remote-server</c> via <see cref="RpcClient"/>.
    /// <para>
    /// Because the RPC <c>exec_git</c> method returns the full stdout/stderr in one shot,
    /// streaming reads (<see cref="Start"/>) are emulated by buffering the output into a
    /// <see cref="MemoryStream"/>. This is fine for read-only queries (log/status/show/branch)
    /// whose output is bounded; a streaming RPC variant can be added later if needed.
    /// </para>
    /// <para>
    /// Stdin-driven commands (pathspec/patch/LFS smudge) and detached launch (difftool) are
    /// not supported here yet — they are handled in later phases.
    /// </para>
    /// </summary>
    public sealed class RemoteCommandRunner : ICommandRunner
    {
        private readonly RpcClient _client;

        public RemoteCommandRunner(RpcClient client)
        {
            _client = client;
        }

        public ICommandProcess Start(Command.RunSpec spec)
        {
            if (spec.RedirectStandardInput)
                throw new NotSupportedException("RemoteCommandRunner.Start does not support stdin (handled in a later phase)");

            // Use the streaming RPC so large outputs (git log) arrive in chunks instead of one
            // giant JSON string; the client-side process exposes a blocking stream that the
            // command parsers read line-by-line, exactly like a local git process.
            var streamId = Guid.NewGuid().ToString();
            var proc = new RemoteStreamProcess(_client, streamId);

            var parameters = new
            {
                stream_id = streamId,
                args = spec.Args,
                working_dir = spec.WorkingDirectory,
                ssh_key = spec.SSHKey,
                editor = spec.Editor.ToString(),
                stdin = spec.StdinContent,
            };

            // The handler is already registered before the call, so no data chunks are lost.
            _client.Call("exec_git_stream", parameters);
            return proc;
        }

        public async Task<int> RunForExitCodeAsync(Command.RunSpec spec, CancellationToken ct)
        {
            var r = await Task.Run(() => ExecGit(spec)).ConfigureAwait(false);
            return r.ExitCode;
        }

        public void StartDetached(Command.RunSpec spec)
        {
            throw new NotSupportedException("External diff/merge tools are not supported for remote repositories");
        }

        public Command.Result ReadToEnd(Command.RunSpec spec)
        {
            var r = ExecGit(spec);
            return new Command.Result { IsSuccess = r.ExitCode == 0, StdOut = r.Stdout, StdErr = r.Stderr };
        }

        public async Task<Command.Result> ReadToEndAsync(Command.RunSpec spec, CancellationToken ct)
        {
            var r = await Task.Run(() => ExecGit(spec)).ConfigureAwait(false);
            return new Command.Result { IsSuccess = r.ExitCode == 0, StdOut = r.Stdout, StdErr = r.Stderr };
        }

        public async Task<bool> ExecAsync(Command.RunSpec spec, Action<string> onOutput, CancellationToken ct)
        {
            var r = await Task.Run(() => ExecGit(spec)).ConfigureAwait(false);
            EmitLines(r.Stdout, onOutput);
            EmitLines(r.Stderr, onOutput);
            return r.ExitCode == 0;
        }

        private static void EmitLines(string text, Action<string> onOutput)
        {
            if (string.IsNullOrEmpty(text))
                return;

            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
                onOutput(line);
        }

        private ExecGitResult ExecGit(Command.RunSpec spec)
        {
            var parameters = new
            {
                args = spec.Args,
                working_dir = spec.WorkingDirectory,
                ssh_key = spec.SSHKey,
                editor = spec.Editor.ToString(),
                stdin = spec.StdinContent,
            };

            var result = _client.Call("exec_git", parameters);
            if (result == null)
                return new ExecGitResult();

            // Pull fields directly from the JsonNode instead of round-tripping through
            // ToJsonString()+Deserialize, which would allocate an extra large string for big
            // git log outputs and cause GC pressure / UI jank.
            return new ExecGitResult
            {
                Stdout = result["stdout"]?.GetValue<string>() ?? string.Empty,
                Stderr = result["stderr"]?.GetValue<string>() ?? string.Empty,
                ExitCode = result["exit_code"]?.GetValue<int>() ?? -1,
            };
        }

        private sealed class RemoteStreamProcess : ICommandProcess
        {
            private readonly RpcClient _client;
            private readonly string _streamId;
            private readonly BlockingCollection<byte[]> _blocks = new();
            private readonly BlockingStream _stream;
            private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private StreamReader _stdoutReader;
            private StringReader _stderrReader;
            private int _exitCode = -1;
            private string _stderr = string.Empty;

            public RemoteStreamProcess(RpcClient client, string streamId)
            {
                _client = client;
                _streamId = streamId;
                _stream = new BlockingStream(_blocks);

                _client.RegisterStreamHandler(streamId, OnStreamNotification);
            }

            private void OnStreamNotification(string method, JsonNode pars)
            {
                if (method == "exec_git_stream_data")
                {
                    var data = pars?["data"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(data))
                    {
                        try { _blocks.Add(Convert.FromBase64String(data)); }
                        catch { /* ignore bad chunk */ }
                    }
                }
                else if (method == "exec_git_stream_done")
                {
                    _exitCode = pars?["exit_code"]?.GetValue<int>() ?? -1;
                    _stderr = pars?["stderr"]?.GetValue<string>() ?? string.Empty;
                    _blocks.CompleteAdding();
                    _done.TrySetResult();
                }
            }

            public TextReader Stdout => _stdoutReader ??= new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
            public Stream StdoutStream => _stream;
            public StreamWriter Stdin => throw new NotSupportedException("remote process has no stdin via Start");
            public TextReader Stderr => _stderrReader ??= new StringReader(_stderr);
            public Task WaitForExitAsync(CancellationToken ct) => _done.Task;
            public int ExitCode => _exitCode;

            public void Dispose()
            {
                _client.UnregisterStreamHandler(_streamId);
                _blocks.CompleteAdding();
                _stdoutReader?.Dispose();
                _stderrReader?.Dispose();
                _stream.Dispose();
            }
        }

        /// <summary>
        /// A read-only stream backed by a <see cref="BlockingCollection{Byte}"/> of chunks.
        /// <see cref="Read"/> blocks until at least one byte is available or the collection is
        /// completed. This lets a <see cref="StreamReader"/> read lines incrementally from a
        /// remote streaming RPC, matching local process stdout behavior.
        /// </summary>
        private sealed class BlockingStream : Stream
        {
            private readonly BlockingCollection<byte[]> _blocks;
            private byte[] _current = Array.Empty<byte>();
            private int _pos = 0;

            public BlockingStream(BlockingCollection<byte[]> blocks)
            {
                _blocks = blocks;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int written = 0;
                while (written < count)
                {
                    if (_pos >= _current.Length)
                    {
                        if (!_blocks.TryTake(out _current, Timeout.Infinite))
                        {
                            // Collection completed and empty.
                            break;
                        }
                        _pos = 0;
                    }

                    var n = Math.Min(count - written, _current.Length - _pos);
                    Array.Copy(_current, _pos, buffer, offset + written, n);
                    _pos += n;
                    written += n;
                }

                return written;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
