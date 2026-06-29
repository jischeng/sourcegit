namespace SourceGit.Commands
{
    public class Add : Command
    {
        public Add(string repo, string pathspec)
        {
            WorkingDirectory = repo;
            Context = repo;
            Stdin = pathspec;
            Args = "add --force --verbose --pathspec-from-file=-";
        }
    }
}
