using System.Text.Json.Serialization;

namespace SourceGit.Models
{
    /// <summary>
    /// Configuration for a remote host that serves repositories over SSH.
    /// <para>
    /// <see cref="Host"/> is passed straight to <c>ssh</c>, so it should be either a
    /// <c>user@host</c> pair or — preferred — an alias defined in the user's
    /// <c>~/.ssh/config</c>. Reusing the SSH config means ProxyJump (jump hosts / multi-hop),
    /// identity files, agent forwarding, port and passwordless auth are all honored exactly
    /// as the user already configured them, without bespoke options here.
    /// </para>
    /// </summary>
    public class RemoteHost
    {
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        /// <summary>Absolute path to the sourcegit executable on the remote host.</summary>
        [JsonPropertyName("remote_server_path")]
        public string RemoteServerPath { get; set; } = "sourcegit";
    }
}
