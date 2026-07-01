using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryCurrentBranchCommitHashes : Command
    {
        public QueryCurrentBranchCommitHashes(string repo, ulong sinceTimestamp)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"log --since=@{sinceTimestamp} --format=%H";
        }

        public async Task<HashSet<string>> GetResultAsync()
        {
            var outs = new HashSet<string>();

            try
            {
                using var proc = Runner.Start(BuildSpec());

                while (await proc.Stdout.ReadLineAsync().ConfigureAwait(false) is { Length: > 8 } line)
                    outs.Add(line);

                await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignore exceptions;
            }

            return outs;
        }
    }
}
