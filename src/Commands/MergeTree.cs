using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class MergeTree : Command
    {
        public MergeTree(string repo, string source, string dest)
        {
            WorkingDirectory = repo;
            Args = $"merge-tree --write-tree {source} {dest}";
        }

        public async Task<int> GetExitCodeAsync()
        {
            return await Runner.RunForExitCodeAsync(BuildSpec(), CancellationToken.None).ConfigureAwait(false);
        }
    }
}
