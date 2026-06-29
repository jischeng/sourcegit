using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryRevisionFileNames : Command
    {
        public QueryRevisionFileNames(string repo, string revision)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"ls-tree -r --name-only {revision}";
        }

        public async Task<List<string>> GetResultAsync()
        {
            var outs = new List<string>();

            try
            {
                using var proc = Runner.Start(BuildSpec());

                while (await proc.Stdout.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 } line)
                    outs.Add(line);

                await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignore exceptions.
            }

            return outs;
        }
    }
}
