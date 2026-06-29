using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    /// Abstracts how a git command is actually launched.
    /// <para>
    /// The local implementation (<see cref="LocalCommandRunner"/>) wraps the existing
    /// <c>Process.Start(git)</c> behavior and keeps everything unchanged for local
    /// repositories. A future remote implementation will forward the same calls to a
    /// lightweight server over an SSH channel, so that a repository living on a remote
    /// host can be managed without cloning it locally.
    /// </para>
    /// </summary>
    public interface ICommandRunner
    {
        /// <summary>
        /// Start a git process and return a handle that exposes its stdout stream for
        /// line-by-line or binary streaming reads. Used by query/diff commands that
        /// parse output incrementally.
        /// </summary>
        ICommandProcess Start(Command.RunSpec spec);

        /// <summary>
        /// Run a git process to completion without capturing output, returning only the
        /// exit code. Used by commands (replay/merge-tree) that only care about whether
        /// the operation succeeded.
        /// </summary>
        Task<int> RunForExitCodeAsync(Command.RunSpec spec, CancellationToken ct);

        /// <summary>
        /// Start a git process fire-and-forget: do not wait, do not capture output.
        /// Used by difftool which launches an external GUI tool and returns immediately.
        /// </summary>
        void StartDetached(Command.RunSpec spec);

        /// <summary>
        /// Run a git process to completion, capturing all stdout/stderr synchronously.
        /// Replaces <see cref="Command.ReadToEnd"/>.
        /// </summary>
        Command.Result ReadToEnd(Command.RunSpec spec);

        /// <summary>
        /// Run a git process to completion, capturing all stdout/stderr asynchronously.
        /// Replaces <see cref="Command.ReadToEndAsync"/>.
        /// </summary>
        Task<Command.Result> ReadToEndAsync(Command.RunSpec spec, CancellationToken ct);

        /// <summary>
        /// Run a git process, invoking <paramref name="onOutput"/> for each line emitted
        /// on stdout or stderr. The callback receives raw lines (including <c>null</c> for
        /// the end-of-stream marker); the caller owns filtering and logging. Returns
        /// <c>true</c> when the process exits with code 0.
        /// <para>
        /// Replaces the streaming portion of <see cref="Command.ExecAsync"/>. The error
        /// collection, progress filtering and notification raising stay in the
        /// <see cref="Command"/> layer via the callback.
        /// </para>
        /// </summary>
        Task<bool> ExecAsync(Command.RunSpec spec, Action<string> onOutput, CancellationToken ct);
    }

    /// <summary>
    /// Handle to a running git process started via <see cref="ICommandRunner.Start"/>.
    /// Allows streaming stdout reads and waiting for exit. Dispose to release the
    /// underlying process.
    /// </summary>
    public interface ICommandProcess : IDisposable
    {
        /// <summary>Text reader over stdout, for <c>ReadLineAsync</c> usage.</summary>
        StreamReader Stdout { get; }

        /// <summary>Raw stdout stream (= <see cref="Stdout"/>'s BaseStream), for binary <c>CopyToAsync</c> usage.</summary>
        Stream StdoutStream { get; }

        /// <summary>
        /// Text writer over stdin. Only valid when <see cref="Command.RunSpec.RedirectStandardInput"/>
        /// was set on the spec passed to <see cref="ICommandRunner.Start"/>.
        /// </summary>
        StreamWriter Stdin { get; }

        /// <summary>Text reader over stderr.</summary>
        StreamReader Stderr { get; }

        /// <summary>Wait for the process to exit.</summary>
        Task WaitForExitAsync(CancellationToken ct);

        /// <summary>Exit code after the process has exited.</summary>
        int ExitCode { get; }
    }
}
