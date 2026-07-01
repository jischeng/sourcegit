using System;
using System.IO;
using System.Text;
using System.Text.Json;
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

            var r = ExecGit(spec);
            return new RemoteCommandProcess(r.Stdout ?? string.Empty, r.Stderr ?? string.Empty, r.ExitCode);
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

        private sealed class RemoteCommandProcess : ICommandProcess
        {
            private readonly string _stdout;
            private readonly string _stderr;
            private readonly int _exitCode;
            private StringReader _stdoutReader;
            private StringReader _stderrReader;

            public RemoteCommandProcess(string stdout, string stderr, int exitCode)
            {
                _stdout = stdout;
                _stderr = stderr;
                _exitCode = exitCode;
            }

            // Line-based callers (QueryCommits/CompareRevisions/...) read through Stdout as a
            // TextReader. Using StringReader avoids converting the whole stdout to a byte[]
            // and back, which was the main source of GC pressure for large git log outputs.
            public TextReader Stdout => _stdoutReader ??= new StringReader(_stdout);

            // Binary/copy callers (Diff) use StdoutStream. This is rare and the output is
            // bounded, so allocating a byte[] here is acceptable.
            public Stream StdoutStream => new MemoryStream(Encoding.UTF8.GetBytes(_stdout), writable: false);
            public StreamWriter Stdin => throw new NotSupportedException("remote process has no stdin via Start");
            public TextReader Stderr => _stderrReader ??= new StringReader(_stderr);
            public Task WaitForExitAsync(CancellationToken ct) => Task.CompletedTask;
            public int ExitCode => _exitCode;

            public void Dispose()
            {
                _stdoutReader?.Dispose();
                _stderrReader?.Dispose();
            }
        }
    }
}
