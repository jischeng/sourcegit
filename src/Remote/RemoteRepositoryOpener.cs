using System;
using System.IO;

using SourceGit.Commands;
using SourceGit.ViewModels;

namespace SourceGit.Remote
{
    /// <summary>
    /// Opens a repository that lives on a remote host. Auto-deploys the server binary to
    /// <c>~/.sourcegit-server/sourcegit</c> on the remote (probe → upload if missing → chmod),
    /// then establishes the SSH connection, registers a <see cref="RemoteCommandRunner"/>,
    /// probes isBare/gitDir via RPC, and constructs the <see cref="Repository"/>.
    /// <para>
    /// Probe/upload/chmod reuse the same ssh host alias so ~/.ssh/config (ProxyJump/jump
    /// hosts, agent, identity, passwordless auth) applies throughout.
    /// </para>
    /// </summary>
    public static class RemoteRepositoryOpener
    {
        // Fixed command that launches the auto-deployed server on the remote host.
        private const string RemoteServerCommand = "~/.sourcegit-server/sourcegit --remote-server";

        public static Repository Open(Models.RemoteHost host, string remotePath)
        {
            EnsureRemoteServer(host.Host);

            var conn = new SshConnection(host.Host, RemoteServerCommand);
            var client = new RpcClient(conn.Input, conn.Output);
            var runner = new RemoteCommandRunner(client);

            // Verify the server is alive before doing anything else.
            client.Call("ping", new { });

            // Register the runner so any Commands spawned with remotePath reach the remote.
            CommandRunnerRegistry.Register(remotePath, runner);

            // Probe isBare + gitDir via RPC.
            var isBareRS = runner.ReadToEnd(new Command.RunSpec
            {
                Args = "rev-parse --is-bare-repository",
                WorkingDirectory = remotePath,
            });
            var isBare = isBareRS.IsSuccess && isBareRS.StdOut.Trim() == "true";

            var gitDirRS = runner.ReadToEnd(new Command.RunSpec
            {
                Args = "rev-parse --git-dir",
                WorkingDirectory = remotePath,
            });
            var gitDir = gitDirRS.IsSuccess ? gitDirRS.StdOut.Trim() : remotePath;
            if (string.IsNullOrEmpty(gitDir) || gitDir == ".")
                gitDir = remotePath;
            else if (!gitDir.StartsWith('/'))
                gitDir = remotePath.TrimEnd('/') + "/" + gitDir;

            var repo = new Repository(isBare, remotePath, gitDir, isRemote: true);
            repo.RemoteConnection = conn;
            repo.FileSystem = new RemoteFileSystem(client);
            repo.RemoteWatcher = new RemoteWatcher(repo, client);
            client.Call("watch_start", new { path = remotePath });
            return repo;
        }

        /// <summary>
        /// Ensure the server binary exists and is executable on the remote host. Probes first
        /// so the common case (already deployed) needs no upload; uploads the bundled binary
        /// if missing. Throws on failure.
        /// </summary>
        private static void EnsureRemoteServer(string host)
        {
            var probe = SshExec.Run(host, "test -x ~/.sourcegit-server/sourcegit && echo OK");
            if (probe.stdout.Trim() == "OK")
                return;

            var local = Native.OS.GetBundledRemoteServerPath();
            if (string.IsNullOrEmpty(local))
                throw new Exception("bundled remote server binary not found; please run from the installed app");

            var mkdir = SshExec.Run(host, "mkdir -p ~/.sourcegit-server");
            if (mkdir.exitCode != 0)
                throw new Exception($"failed to create ~/.sourcegit-server on '{host}' (exit {mkdir.exitCode})");

            if (ScpUpload.Upload(host, local, "~/.sourcegit-server/sourcegit") != 0)
                throw new Exception($"failed to upload server binary to '{host}'");

            var chmod = SshExec.Run(host, "chmod +x ~/.sourcegit-server/sourcegit");
            if (chmod.exitCode != 0)
                throw new Exception($"failed to chmod server binary on '{host}' (exit {chmod.exitCode})");
        }
    }
}
