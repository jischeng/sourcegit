using System;
using System.Threading.Tasks;

using SourceGit.Commands;
using SourceGit.ViewModels;

namespace SourceGit.Remote
{
    /// <summary>
    /// Opens a repository that lives on a remote host by reusing the already-established
    /// <see cref="RemoteHostSession"/> for that host (connected from the settings page).
    /// <para>
    /// The connection is owned by the session, not by the repository, so several repositories
    /// can share one connection and closing a repository tab never tears down the connection.
    /// This method registers a <see cref="RemoteCommandRunner"/> for the repository path,
    /// probes isBare/gitDir over RPC, and constructs the <see cref="Repository"/>.
    /// </para>
    /// </summary>
    public static class RemoteRepositoryOpener
    {
        public static Repository Open(Models.RemoteHost host, string remotePath)
        {
            var session = RemoteHostManager.Instance.GetConnectedSession(host);
            if (session == null)
            {
                // Auto-connect when opening a remote repository (restored tabs, recent repos, etc.).
                // This is called from a background thread by the launcher, so blocking here is fine
                // and does not stall the UI.
                var connected = RemoteHostManager.Instance.ConnectAsync(host).GetAwaiter().GetResult();

                if (!connected)
                    throw new Exception($"Host '{host?.Name ?? host?.Host}' could not be connected automatically.");

                session = RemoteHostManager.Instance.GetConnectedSession(host);
                if (session == null)
                    throw new Exception($"Host '{host?.Name ?? host?.Host}' is not connected.");
            }

            var client = session.Client;
            var runner = new RemoteCommandRunner(client);

            // Register the runner so any Commands spawned with remotePath reach the remote.
            CommandRunnerRegistry.Register(remotePath, runner);

            try
            {
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

                var repo = new Repository(isBare, remotePath, gitDir, isRemote: true)
                {
                    FileSystem = new RemoteFileSystem(client),
                };
                repo.RemoteWatcher = new RemoteWatcher(repo, client);
                client.Call("watch_start", new { path = remotePath });
                return repo;
            }
            catch
            {
                CommandRunnerRegistry.Unregister(remotePath);
                throw;
            }
        }
    }
}
