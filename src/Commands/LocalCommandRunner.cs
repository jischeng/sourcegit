using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    /// <see cref="ICommandRunner"/> implementation that launches git as a local child
    /// process. This is a faithful relocation of the process logic that previously lived
    /// inline in <see cref="Command"/>: the environment setup (SSH askpass, GIT_SSH_COMMAND,
    /// locale), the global git arguments and the editor injection are kept identical so
    /// that local repositories behave exactly as before.
    /// </summary>
    public sealed class LocalCommandRunner : ICommandRunner
    {
        public static readonly LocalCommandRunner Instance = new LocalCommandRunner();

        public ICommandProcess Start(Command.RunSpec spec)
        {
            var proc = new Process();
            proc.StartInfo = BuildStartInfo(spec, redirect: true);
            proc.Start();
            return new LocalCommandProcess(proc);
        }

        public async Task<int> RunForExitCodeAsync(Command.RunSpec spec, CancellationToken ct)
        {
            using var proc = new Process();
            proc.StartInfo = BuildStartInfo(spec, redirect: false);

            try
            {
                proc.Start();
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                return proc.ExitCode;
            }
            catch
            {
                // Ignore any exceptions and just return -1, matching previous behavior.
                return -1;
            }
        }

        public void StartDetached(Command.RunSpec spec)
        {
            // Fire-and-forget: caller does not wait and does not capture output.
            Process.Start(BuildStartInfo(spec, redirect: false));
        }

        public Command.Result ReadToEnd(Command.RunSpec spec)
        {
            using var proc = new Process();
            proc.StartInfo = BuildStartInfo(spec, redirect: true);

            try
            {
                proc.Start();
            }
            catch (Exception e)
            {
                return Command.Result.Failed(e.Message);
            }

            if (spec.StdinContent != null)
            {
                proc.StandardInput.Write(spec.StdinContent);
                proc.StandardInput.Close();
            }

            var rs = new Command.Result { IsSuccess = true };
            rs.StdOut = proc.StandardOutput.ReadToEnd();
            rs.StdErr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            rs.IsSuccess = proc.ExitCode == 0;
            return rs;
        }

        public async Task<Command.Result> ReadToEndAsync(Command.RunSpec spec, CancellationToken ct)
        {
            using var proc = new Process();
            proc.StartInfo = BuildStartInfo(spec, redirect: true);

            try
            {
                proc.Start();
            }
            catch (Exception e)
            {
                return Command.Result.Failed(e.Message);
            }

            if (spec.StdinContent != null)
            {
                await proc.StandardInput.WriteAsync(spec.StdinContent).ConfigureAwait(false);
                proc.StandardInput.Close();
            }

            var rs = new Command.Result { IsSuccess = true };
            rs.StdOut = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            rs.StdErr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            rs.IsSuccess = proc.ExitCode == 0;
            return rs;
        }

        public async Task<bool> ExecAsync(Command.RunSpec spec, Action<string> onOutput, CancellationToken ct)
        {
            using var proc = new Process();
            proc.StartInfo = BuildStartInfo(spec, redirect: true);
            proc.OutputDataReceived += (_, e) => onOutput(e.Data);
            proc.ErrorDataReceived += (_, e) => onOutput(e.Data);

            var captured = new CapturedProcess { Process = proc };
            var capturedLock = new object();
            try
            {
                proc.Start();

                // Not safe, please only use `CancellationToken` in readonly commands.
                if (ct.CanBeCanceled)
                {
                    ct.Register(() =>
                    {
                        lock (capturedLock)
                        {
                            if (captured is { Process: { HasExited: false } })
                                captured.Process.Kill();
                        }
                    });
                }
            }
            catch (Exception e)
            {
                onOutput(e.Message);
                return false;
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (spec.StdinContent != null)
            {
                await proc.StandardInput.WriteAsync(spec.StdinContent).ConfigureAwait(false);
                proc.StandardInput.Close();
            }

            try
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                onOutput(e.Message);
            }

            lock (capturedLock)
            {
                captured.Process = null;
            }

            if (!ct.IsCancellationRequested && proc.ExitCode != 0)
                return false;

            return true;
        }

        private ProcessStartInfo BuildStartInfo(Command.RunSpec spec, bool redirect)
        {
            var start = new ProcessStartInfo();
            start.FileName = Native.OS.GitExecutable;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;

            if (redirect)
            {
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = Encoding.UTF8;
                start.StandardErrorEncoding = Encoding.UTF8;
            }

            if (spec.RedirectStandardInput || spec.StdinContent != null)
                start.RedirectStandardInput = true;

            var selfExecFile = Environment.ProcessPath;

            // Force using this app as SSH askpass program (GUI). Only for the local client;
            // the headless remote server skips this so ssh uses the agent/identity from the
            // user's ssh config instead of a non-existent GUI askpass.
            if (!spec.Headless)
            {
                start.Environment.Add("SSH_ASKPASS", selfExecFile); // Can not use parameter here, because it invoked by SSH with `exec`
                start.Environment.Add("SSH_ASKPASS_REQUIRE", "prefer");
                start.Environment.Add("SOURCEGIT_LAUNCH_AS_ASKPASS", "TRUE");
                if (!OperatingSystem.IsLinux())
                    start.Environment.Add("DISPLAY", "required");
            }

            // If an SSH private key was provided, sets the environment.
            if (!start.Environment.ContainsKey("GIT_SSH_COMMAND") && !string.IsNullOrEmpty(spec.SSHKey))
                start.Environment.Add("GIT_SSH_COMMAND", $"ssh -i '{spec.SSHKey}' -F '/dev/null'");

            // Force using en_US.UTF-8 locale
            if (OperatingSystem.IsLinux())
            {
                start.Environment.Add("LANG", "C");
                start.Environment.Add("LC_ALL", "C");
            }

            var builder = new StringBuilder(2048);
            builder
                .Append("--no-pager -c core.quotepath=off -c credential.helper=")
                .Append(Native.OS.CredentialHelper)
                .Append(' ');

            switch (spec.Editor)
            {
                case Command.EditorType.CoreEditor:
                    builder.Append($"""-c core.editor="\"{selfExecFile}\" --core-editor" """);
                    break;
                case Command.EditorType.RebaseEditor:
                    builder.Append($"""-c core.editor="\"{selfExecFile}\" --rebase-message-editor" -c sequence.editor="\"{selfExecFile}\" --rebase-todo-editor" -c rebase.abbreviateCommands=true """);
                    break;
                default:
                    builder.Append("-c core.editor=true ");
                    break;
            }

            builder.Append(spec.Args);
            start.Arguments = builder.ToString();

            // Working directory
            if (!string.IsNullOrEmpty(spec.WorkingDirectory))
                start.WorkingDirectory = spec.WorkingDirectory;

            return start;
        }

        private sealed class LocalCommandProcess : ICommandProcess
        {
            private readonly Process _proc;

            public LocalCommandProcess(Process proc)
            {
                _proc = proc;
            }

            public StreamReader Stdout => _proc.StandardOutput;
            public Stream StdoutStream => _proc.StandardOutput.BaseStream;
            public StreamWriter Stdin => _proc.StandardInput;
            public StreamReader Stderr => _proc.StandardError;
            public Task WaitForExitAsync(CancellationToken ct) => _proc.WaitForExitAsync(ct);
            public int ExitCode => _proc.ExitCode;

            public void Dispose()
            {
                _proc.Dispose();
            }
        }

        private sealed class CapturedProcess
        {
            public Process Process { get; set; }
        }
    }
}
