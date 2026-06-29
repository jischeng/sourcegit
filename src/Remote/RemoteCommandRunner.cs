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
            var stdout = new MemoryStream(Encoding.UTF8.GetBytes(r.Stdout ?? string.Empty), writable: false);
            var stderr = new MemoryStream(Encoding.UTF8.GetBytes(r.Stderr ?? string.Empty), writable: false);
            return new RemoteCommandProcess(stdout, stderr, r.ExitCode);
        }

        public async Task<int> RunForExitCodeAsync(Command.RunSpec spec, CancellationToken ct)
        {
            var r = await Task.Run(() => ExecGit(spec)).ConfigureAwait(false);
            return r.ExitCode;
        }

        public void StartDetached(Command.RunSpec spec)
        {
            throw new NotSupportedException("Remote detached start is not supported (difftool must run locally)");
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
            };

            var result = _client.Call("exec_git", parameters);
            if (result == null)
                return new ExecGitResult();

            return JsonSerializer.Deserialize<ExecGitResult>(result.ToJsonString());
        }

        private sealed class RemoteCommandProcess : ICommandProcess
        {
            private readonly MemoryStream _stdout;
            private readonly MemoryStream _stderr;
            private readonly int _exitCode;

            public RemoteCommandProcess(MemoryStream stdout, MemoryStream stderr, int exitCode)
            {
                _stdout = stdout;
                _stderr = stderr;
                _exitCode = exitCode;
            }

            public StreamReader Stdout => new StreamReader(_stdout, Encoding.UTF8, leaveOpen: true);
            public Stream StdoutStream => _stdout;
            public StreamWriter Stdin => throw new NotSupportedException("remote process has no stdin via Start");
            public StreamReader Stderr => new StreamReader(_stderr, Encoding.UTF8, leaveOpen: true);
            public Task WaitForExitAsync(CancellationToken ct) => Task.CompletedTask;
            public int ExitCode => _exitCode;

            public void Dispose()
            {
                _stdout.Dispose();
                _stderr.Dispose();
            }
        }
    }
}
