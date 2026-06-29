using System;
using System.IO;

using SourceGit.Commands;
using SourceGit.ViewModels;

namespace SourceGit.Remote
{
    /// <summary>
    /// Opens a repository that lives on a remote host. Establishes the SSH connection,
    /// registers a <see cref="RemoteCommandRunner"/> for the remote path (so all
    /// <c>Commands.XXX(remotePath,...)</c> reach the server), probes isBare/gitDir via RPC,
    /// and constructs the <see cref="Repository"/>.
    /// <para>
    /// Probing is done with <c>git rev-parse</c> over RPC on purpose: the local
    /// <c>IsBareRepository</c>/<c>GetRepositoryGitDir</c> helpers short-circuit on local
    /// <c>Directory.Exists</c>/<c>File.Exists</c> checks that are meaningless for a path
    /// that does not exist on this machine.
    /// </para>
    /// </summary>
    public static class RemoteRepositoryOpener
    {
        /// <summary>
        /// Open <paramref name="remotePath"/> on <paramref name="host"/>. The returned
        /// Repository owns the SSH connection; disposing it on close releases the process
        /// and unregisters the runner.
        /// </summary>
        public static Repository Open(Models.RemoteHost host, string remotePath)
        {
            var conn = new SshConnection(host.Host, $"{host.RemoteServerPath} --remote-server");
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
            return repo;
        }
    }
}
