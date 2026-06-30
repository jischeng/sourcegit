using System.Text.Json.Serialization;

namespace SourceGit.Models
{
    /// <summary>
    /// Configuration for a remote host that serves repositories over SSH.
    /// <para>
    /// <see cref="Host"/> is passed straight to <c>ssh</c>/<c>scp</c>, so it should be either
    /// a <c>user@host</c> pair or — preferred — an alias defined in the user's
    /// <c>~/.ssh/config</c>. Reusing the SSH config means ProxyJump (jump hosts / multi-hop),
    /// identity files, agent forwarding, port and passwordless auth are all honored exactly
    /// as the user already configured them.
    /// </para>
    /// <para>
    /// The remote server binary is auto-deployed to <c>~/.sourcegit-server/sourcegit</c> on
    /// the remote host (see <see cref="Remote.RemoteRepositoryOpener"/>), so no server path
    /// is configured here.
    /// </para>
    /// </summary>
    public class RemoteHost
    {
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;
    }
}
