namespace SourceGit.Models
{
    /// <summary>
    /// Represents the host a repository lives on.
    /// <para>
    /// A local repository is backed by <see cref="SourceGit.Commands.LocalCommandRunner"/>
    /// and a passthrough filesystem, matching today's behavior exactly. A remote
    /// repository (future SSH support) is backed by an RPC connection to a lightweight
    /// server running on the remote host.
    /// </para>
    /// <para>
    /// Phase 0 only wires the abstraction in place; <see cref="IsRemote"/> is always
    /// <c>false</c> for now and the local implementations are used everywhere.
    /// </para>
    /// </summary>
    public interface IRepositoryHost
    {
        /// <summary>Whether the repository lives on a remote host accessed over SSH.</summary>
        bool IsRemote { get; }

        /// <summary>Runner used to execute git commands for repositories on this host.</summary>
        Commands.ICommandRunner Runner { get; }

        /// <summary>Filesystem accessor for the working tree and <c>.git</c> internals.</summary>
        IFileSystem FileSystem { get; }
    }
}
