using System.Collections.Concurrent;

namespace SourceGit.Commands
{
    /// <summary>
    /// Maps a repository working-directory to the <see cref="ICommandRunner"/> that should
    /// execute git for it.
    /// <para>
    /// Local repositories are not registered and fall back to
    /// <see cref="LocalCommandRunner"/>. A remote repository, when opened, registers its
    /// <see cref="Remote.RemoteCommandRunner"/> here so that every <c>new Commands.XXX(repo,...)</c>
    /// — which only carries the path — still reaches the right runner without changing call
    /// sites. The entry is removed when the repository is closed.
    /// </para>
    /// <para>
    /// Lookup is by normalized path, so the same path string the Command base class sees as
    /// <c>WorkingDirectory</c> resolves to the runner registered by Repository.
    /// </para>
    /// </summary>
    public static class CommandRunnerRegistry
    {
        public static void Register(string workingDirectory, ICommandRunner runner)
        {
            if (string.IsNullOrEmpty(workingDirectory) || runner == null)
                return;

            _runners[Normalize(workingDirectory)] = runner;
        }

        public static void Unregister(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory))
                return;

            _runners.TryRemove(Normalize(workingDirectory), out _);
        }

        public static ICommandRunner Get(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory))
                return null;

            return _runners.TryGetValue(Normalize(workingDirectory), out var runner) ? runner : null;
        }

        private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

        private static readonly ConcurrentDictionary<string, ICommandRunner> _runners = new();
    }
}
