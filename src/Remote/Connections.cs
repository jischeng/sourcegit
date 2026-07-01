using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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
    /// Connection to a server launched as a local child process (testing / local mode).
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
    /// Connection to a remote server reached over SSH. Reuses the user's ~/.ssh/config:
    /// <paramref name="host"/> may be a config alias carrying ProxyJump (jump hosts /
    /// multi-hop), identity, agent, port and passwordless auth.
    /// </summary>
    public sealed class SshConnection : IConnection
    {
        private readonly Process _proc;

        public SshConnection(string host, string remoteServerCommand)
        {
            var args = $"-T -o StrictHostKeyChecking=accept-new {host} {remoteServerCommand}";

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

    /// <summary>
    /// Run a one-shot command on the remote host over SSH (non-interactive: BatchMode so it
    /// never prompts for a password — relies on the ssh config's key/agent). Used to probe
    /// for the deployed server binary and to mkdir/chmod around the upload.
    /// </summary>
    public static class SshExec
    {
        public static (string stdout, int exitCode) Run(string host, string command)
        {
            var args = $"-T -o BatchMode=yes -o StrictHostKeyChecking=accept-new {host} {command}";
            var psi = new ProcessStartInfo("ssh", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null)
                    return (string.Empty, -1);

                // Drain both streams concurrently; reading only stdout can deadlock if the
                // remote command writes enough to stderr to fill the OS pipe buffer.
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);
                return (stdoutTask.Result, p.ExitCode);
            }
            catch
            {
                return (string.Empty, -1);
            }
        }
    }

    /// <summary>
    /// Upload a local file to the remote host via scp, reusing the same host alias so
    /// ~/.ssh/config (ProxyJump/agent/key) applies. Non-interactive (BatchMode).
    /// </summary>
    public static class ScpUpload
    {
        public static int Upload(string host, string localFile, string remotePath)
        {
            // Prefer scp; it is the common case and handles progress/permissions natively.
            var scpCode = TryScp(host, localFile, remotePath);
            if (scpCode == 0)
                return 0;

            // Fallback for minimal/container hosts where scp is not installed: stream the file
            // over a plain ssh channel with `cat > remote`. Works everywhere ssh works.
            return UploadViaSshCat(host, localFile, remotePath);
        }

        private static int TryScp(string host, string localFile, string remotePath)
        {
            var args = $"-o BatchMode=yes -o StrictHostKeyChecking=accept-new \"{localFile}\" {host}:{remotePath}";
            var psi = new ProcessStartInfo("scp", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null)
                    return -1;

                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);
                return p.ExitCode;
            }
            catch
            {
                return -1;
            }
        }

        private static int UploadViaSshCat(string host, string localFile, string remotePath)
        {
            var args = $"-T -o BatchMode=yes -o StrictHostKeyChecking=accept-new {host} \"cat > {remotePath}\"";
            var psi = new ProcessStartInfo("ssh", args)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null)
                    return -1;

                // Stream the local file into ssh stdin; drain stdout/stderr to avoid pipe deadlock.
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                using var fs = File.OpenRead(localFile);
                fs.CopyTo(p.StandardInput.BaseStream);
                p.StandardInput.Close();

                p.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);
                return p.ExitCode;
            }
            catch
            {
                return -1;
            }
        }
    }
}
