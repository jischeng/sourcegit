using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Replay : Command
    {
        public Replay(string repo, string onto, string range)
        {
            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;
            Args = $"replay --onto {onto} {range}";
        }

        public async Task<int> GetExitCodeAsync()
        {
            return await Runner.RunForExitCodeAsync(BuildSpec(), CancellationToken.None).ConfigureAwait(false);
        }
    }
}
