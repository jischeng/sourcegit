using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Commit : Command
    {
        public Commit(string repo, string message, bool signOff, bool noVerify, bool amend, bool resetAuthor)
        {
            WorkingDirectory = repo;
            Context = repo;
            Stdin = message;

            var builder = new StringBuilder();
            builder.Append("commit --allow-empty --file=- ");

            if (signOff)
                builder.Append("--signoff ");

            if (noVerify)
                builder.Append("--no-verify ");

            if (amend)
            {
                builder.Append("--amend ");
                if (resetAuthor)
                    builder.Append("--reset-author ");
                builder.Append("--no-edit");
            }

            Args = builder.ToString();
        }

        public async Task<bool> RunAsync()
        {
            try
            {
                return await ExecAsync().ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }
    }
}
