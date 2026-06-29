using System;
using System.Diagnostics;
using System.IO;

namespace SourceGit.Remote
{
    /// <summary>
    /// A duplex stream pair to a running <c>sourcegit --remote-server</c> process.
    /// <see cref="Input"/> reads the server's stdout; <see cref="Output"/> writes its stdin.
    /// </summary>
    public interface IConnection : IDisposable
    {
        Stream Input { get; }
        Stream Output { get; }
    }

    /// <summary>
    /// Connection to a server launched as a local child process (e.g.
    /// <c>dotnet SourceGit.dll --remote-server</c>). Mainly used for testing and for a
    /// possible "local server" mode; real remote repositories use <see cref="SshConnection"/>.
    /// </summary>
    public sealed class LocalProcessConnection : IConnection
    {
        private readonly Process _proc;

        public LocalProcessConnection(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            _proc = Process.Start(psi)
                ?? throw new Exception($"failed to start remote server: {fileName} {arguments}");

            Input = _proc.StandardOutput.BaseStream;
            Output = _proc.StandardInput.BaseStream;
        }

        public Stream Input { get; }
        public Stream Output { get; }

        public void Dispose()
        {
            try { if (!_proc.HasExited) _proc.Kill(); } catch { }
            _proc.Dispose();
        }
    }

    /// <summary>
    /// Connection to a remote server reached over SSH. Launches
    /// <c>ssh &lt;user&gt;@&lt;host&gt; &lt;remote-sourcegit&gt; --remote-server</c> and pipes
    /// JSON-RPC over the SSH channel's stdin/stdout. Authentication is non-interactive
    /// (BatchMode) via an identity file or the SSH agent.
    /// </summary>
    public sealed class SshConnection : IConnection
    {
        private readonly Process _proc;

        public SshConnection(string host, string user, int port, string identityFile, string remoteServerCommand)
        {
            var args = "-T -o BatchMode=yes -o StrictHostKeyChecking=accept-new";
            if (port > 0)
                args += $" -p {port}";
            if (!string.IsNullOrEmpty(identityFile))
                args += $" -i \"{identityFile}\"";

            var target = string.IsNullOrEmpty(user) ? host : $"{user}@{host}";
            args += $" {target} {remoteServerCommand}";

            var psi = new ProcessStartInfo("ssh", args)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            _proc = Process.Start(psi) ?? throw new Exception("failed to start ssh");
            Input = _proc.StandardOutput.BaseStream;
            Output = _proc.StandardInput.BaseStream;
        }

        public Stream Input { get; }
        public Stream Output { get; }

        public void Dispose()
        {
            try { if (!_proc.HasExited) _proc.Kill(); } catch { }
            _proc.Dispose();
        }
    }
}
