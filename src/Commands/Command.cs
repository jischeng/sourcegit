using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public partial class Command
    {
        public class Result
        {
            public bool IsSuccess { get; set; } = false;
            public string StdOut { get; set; } = string.Empty;
            public string StdErr { get; set; } = string.Empty;

            public static Result Failed(string reason) => new Result() { StdErr = reason };
        }

        public enum EditorType
        {
            None,
            CoreEditor,
            RebaseEditor,
        }

        /// <summary>
        /// Immutable snapshot of the parameters needed to launch a git command.
        /// Passed to <see cref="ICommandRunner"/> so the runner does not depend on the
        /// mutable <see cref="Command"/> surface.
        /// </summary>
        public class RunSpec
        {
            public string Args = string.Empty;
            public string WorkingDirectory = null;
            public EditorType Editor = EditorType.CoreEditor;
            public string SSHKey = string.Empty;

            /// <summary>
            /// Whether to redirect stdin so the caller can write to <see cref="ICommandProcess.Stdin"/>.
            /// Off by default; set <c>true</c> for commands that take piped input
            /// (pathspec, patch data, LFS smudge pointer).
            /// </summary>
            public bool RedirectStandardInput = false;
        }

        public string Context { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = null;
        public EditorType Editor { get; set; } = EditorType.CoreEditor;
        public string SSHKey { get; set; } = string.Empty;
        public string Args { get; set; } = string.Empty;

        // Only used in `ExecAsync` mode.
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
        public bool RaiseError { get; set; } = true;
        public Models.ICommandLog Log { get; set; } = null;

        /// <summary>
        /// Runner that actually launches git. Defaults to local process execution
        /// (<see cref="LocalCommandRunner"/>); future remote support will substitute an
        /// RPC-backed runner per repository host.
        /// </summary>
        protected ICommandRunner Runner { get; set; } = LocalCommandRunner.Instance;

        /// <summary>
        /// Snapshot the current execution parameters into a <see cref="RunSpec"/> for the runner.
        /// </summary>
        protected RunSpec BuildSpec() => new RunSpec
        {
            Args = Args,
            WorkingDirectory = WorkingDirectory,
            Editor = Editor,
            SSHKey = SSHKey,
        };

        public async Task<bool> ExecAsync()
        {
            Log?.AppendLine($"$ git {Args}\n");

            var errs = new List<string>();
            var ok = await Runner.ExecAsync(BuildSpec(), line => HandleOutput(line, errs), CancellationToken).ConfigureAwait(false);

            Log?.AppendLine(string.Empty);

            if (!ok && !CancellationToken.IsCancellationRequested && RaiseError)
            {
                var errMsg = string.Join("\n", errs).Trim();
                if (!string.IsNullOrEmpty(errMsg))
                    RaiseException(errMsg);
            }

            return ok;
        }

        protected Result ReadToEnd()
        {
            return Runner.ReadToEnd(BuildSpec());
        }

        protected async Task<Result> ReadToEndAsync()
        {
            return await Runner.ReadToEndAsync(BuildSpec(), CancellationToken).ConfigureAwait(false);
        }

        protected void RaiseException(string error)
        {
            Models.Notification.Send(Context, error, true);
        }

        private void HandleOutput(string line, List<string> errs)
        {
            if (line == null)
                return;

            Log?.AppendLine(line);

            // Lines to hide in error message.
            if (line.Length > 0)
            {
                if (line.StartsWith("remote: Enumerating objects:", StringComparison.Ordinal) ||
                    line.StartsWith("remote: Counting objects:", StringComparison.Ordinal) ||
                    line.StartsWith("remote: Compressing objects:", StringComparison.Ordinal) ||
                    line.StartsWith("Filtering content:", StringComparison.Ordinal) ||
                    line.StartsWith("hint:", StringComparison.Ordinal))
                    return;

                if (REG_PROGRESS().IsMatch(line))
                    return;
            }

            errs.Add(line);
        }

        [GeneratedRegex(@"\d+%")]
        private static partial Regex REG_PROGRESS();
    }
}
