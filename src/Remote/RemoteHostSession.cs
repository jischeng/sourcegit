using System;

namespace SourceGit.Remote
{
    /// <summary>
    /// A single live connection to one remote host.
    /// <para>
    /// Owns the underlying SSH <see cref="IConnection"/> and the <see cref="RpcClient"/> that
    /// speaks to <c>sourcegit --remote-server</c> running on the host. The session is created
    /// and torn down explicitly from the settings page (Test / Connect / Disconnect / Reset)
    /// and is shared by every repository opened on that host, so the connection outlives any
    /// individual repository tab.
    /// </para>
    /// <para>
    /// All ssh/scp operations reuse the host alias, so <c>~/.ssh/config</c> (ProxyJump / jump
    /// hosts, agent, identity, passwordless auth) applies throughout.
    /// </para>
    /// </summary>
    public sealed class RemoteHostSession : IDisposable
    {
        // Fixed command that launches the auto-deployed server on the remote host.
        // `exec` replaces the remote login shell with the server process so that when the
        // SSH channel closes the server receives SIGHUP and exits instead of being orphaned
        // (some shells, e.g. fish, leave the child running otherwise).
        private const string RemoteServerCommand = "exec ~/.sourcegit-server/sourcegit --remote-server";
        private const string RemoteServerDir = "~/.sourcegit-server";
        private const string RemoteServerBinary = "~/.sourcegit-server/sourcegit";

        public RemoteHostSession(Models.RemoteHost host)
        {
            Host = host;
        }

        public Models.RemoteHost Host { get; }

        /// <summary>The live RPC client, or <c>null</c> when not connected.</summary>
        public RpcClient Client { get; private set; }

        public bool IsConnected => Client != null;

        /// <summary>
        /// Probe connectivity without deploying or starting the server. Runs a trivial
        /// non-interactive command over ssh and reports whether it succeeded.
        /// </summary>
        public (bool ok, string message) Test()
        {
            var alias = Host.Host;
            if (string.IsNullOrWhiteSpace(alias))
                return (false, "Host is empty");

            var (stdout, exit) = SshExec.Run(alias, "echo SOURCEGIT_OK");
            if (exit == 0 && stdout.Trim() == "SOURCEGIT_OK")
                return (true, "Reachable");

            return (false, string.IsNullOrWhiteSpace(stdout) ? $"ssh exited with code {exit}" : stdout.Trim());
        }

        /// <summary>
        /// Establish the connection: ensure the server binary is deployed (probe → upload if
        /// missing → chmod), launch it over ssh, and verify it answers a ping. Idempotent —
        /// returns immediately if already connected. Throws on failure.
        /// </summary>
        public void Connect(bool forceRedeploy = false)
        {
            if (IsConnected)
                return;

            var alias = Host.Host;
            if (string.IsNullOrWhiteSpace(alias))
                throw new Exception("Host is empty");

            EnsureRemoteServer(alias, forceRedeploy);

            var conn = new SshConnection(alias, RemoteServerCommand);
            try
            {
                var client = new RpcClient(conn.Input, conn.Output);
                client.Call("ping", new { });
                _conn = conn;
                Client = client;
            }
            catch
            {
                try { conn.Dispose(); } catch { /* best effort */ }
                throw;
            }
        }

        /// <summary>Tear down the connection. Safe to call when already disconnected.</summary>
        public void Disconnect()
        {
            var client = Client;
            Client = null;

            try { client?.Dispose(); } catch { /* best effort */ }
            try { _conn?.Dispose(); } catch { /* best effort */ }
            _conn = null;
        }

        public void Dispose() => Disconnect();

        /// <summary>
        /// Ensure the server binary exists and is executable on the remote host. Probes first
        /// so the common case (already deployed) needs no upload; uploads the bundled binary
        /// if missing or when <paramref name="forceRedeploy"/> is set. Throws on failure.
        /// </summary>
        private static void EnsureRemoteServer(string host, bool forceRedeploy)
        {
            if (!forceRedeploy)
            {
                var probe = SshExec.Run(host, $"test -x {RemoteServerBinary} && echo OK");
                if (probe.stdout.Trim() == "OK")
                    return;
            }

            var local = Native.OS.GetBundledRemoteServerPath();
            if (string.IsNullOrEmpty(local))
                throw new Exception("Bundled remote server binary not found; please run from the installed app.");

            var mkdir = SshExec.Run(host, $"mkdir -p {RemoteServerDir}");
            if (mkdir.exitCode != 0)
                throw new Exception($"Failed to create {RemoteServerDir} on '{host}' (exit {mkdir.exitCode}).");

            if (ScpUpload.Upload(host, local, RemoteServerBinary) != 0)
                throw new Exception($"Failed to upload server binary to '{host}'.");

            var chmod = SshExec.Run(host, $"chmod +x {RemoteServerBinary}");
            if (chmod.exitCode != 0)
                throw new Exception($"Failed to chmod server binary on '{host}' (exit {chmod.exitCode}).");
        }

        private IConnection _conn;
    }
}
