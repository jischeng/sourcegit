using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public abstract class InProgressContext
    {
        public string Name
        {
            get;
            protected set;
        }

        public async Task ContinueAsync(CommandLog log)
        {
            if (_continueCmd != null)
                await _continueCmd.Use(log).ExecAsync();
        }

        public async Task SkipAsync(CommandLog log)
        {
            if (_skipCmd != null)
                await _skipCmd.Use(log).ExecAsync();
        }

        public async Task AbortAsync(CommandLog log)
        {
            if (_abortCmd != null)
                await _abortCmd.Use(log).ExecAsync();

            OnAborted();
        }

        protected virtual void OnAborted()
        {
        }

        protected Commands.Command _continueCmd = null;
        protected Commands.Command _skipCmd = null;
        protected Commands.Command _abortCmd = null;
    }

    public class CherryPickInProgress : InProgressContext
    {
        public Models.Commit Head
        {
            get;
        }

        public string HeadName
        {
            get;
        }

        public CherryPickInProgress(Repository repo)
        {
            Name = "Cherry-Pick";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.None,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" cherry-pick --continue",
            };

            _skipCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "cherry-pick --skip",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "cherry-pick --abort",
            };

            var headSHA = repo.FileSystem.ReadAllText(Path.Combine(repo.GitDir, "CHERRY_PICK_HEAD")).Trim();
            Head = new Commands.QuerySingleCommit(repo.FullPath, headSHA).GetResult() ?? new Models.Commit() { SHA = headSHA };
            HeadName = Head.GetFriendlyName();
        }
    }

    public class RebaseInProgress : InProgressContext
    {
        public string HeadName
        {
            get;
        }

        public string BaseName
        {
            get;
        }

        public Models.Commit StoppedAt
        {
            get;
        }

        public Models.Commit Onto
        {
            get;
        }

        public RebaseInProgress(Repository repo)
        {
            _gitDir = repo.GitDir;
            _fs = repo.FileSystem;
            Name = "Rebase";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.RebaseEditor,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" rebase --continue",
            };

            _skipCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "rebase --skip",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "rebase --abort",
                RaiseError = false,
            };

            HeadName = repo.FileSystem.ReadAllText(Path.Combine(repo.GitDir, "rebase-merge", "head-name")).Trim();
            if (HeadName.StartsWith("refs/heads/"))
                HeadName = HeadName.Substring(11);
            else if (HeadName.StartsWith("refs/tags/"))
                HeadName = HeadName.Substring(10);

            var stoppedSHAPath = Path.Combine(repo.GitDir, "rebase-merge", "stopped-sha");
            var stoppedSHA = repo.FileSystem.FileExists(stoppedSHAPath)
                ? repo.FileSystem.ReadAllText(stoppedSHAPath).Trim()
                : new Commands.QueryRevisionByRefName(repo.FullPath, HeadName).GetResult();

            if (!string.IsNullOrEmpty(stoppedSHA))
                StoppedAt = new Commands.QuerySingleCommit(repo.FullPath, stoppedSHA).GetResult() ?? new Models.Commit() { SHA = stoppedSHA };

            var ontoSHA = repo.FileSystem.ReadAllText(Path.Combine(repo.GitDir, "rebase-merge", "onto")).Trim();
            Onto = new Commands.QuerySingleCommit(repo.FullPath, ontoSHA).GetResult() ?? new Models.Commit() { SHA = ontoSHA };
            BaseName = Onto.GetFriendlyName();
        }

        protected override void OnAborted()
        {
            var rebaseMergeDir = Path.Combine(_gitDir, "rebase-merge");
            if (_fs.DirectoryExists(rebaseMergeDir))
                _fs.DeleteDirectory(rebaseMergeDir, true);

            var rebaseApplyDir = Path.Combine(_gitDir, "rebase-apply");
            if (_fs.DirectoryExists(rebaseApplyDir))
                _fs.DeleteDirectory(rebaseApplyDir, true);

            var jobFile = Path.Combine(_gitDir, "sourcegit.interactive_rebase");
            if (_fs.FileExists(jobFile))
                _fs.DeleteFile(jobFile);
        }

        private readonly string _gitDir;
        private readonly Models.IFileSystem _fs;
    }

    public class RevertInProgress : InProgressContext
    {
        public Models.Commit Head
        {
            get;
        }

        public RevertInProgress(Repository repo)
        {
            Name = "Revert";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.None,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" revert --continue",
            };

            _skipCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "revert --skip",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "revert --abort",
            };

            var headSHA = repo.FileSystem.ReadAllText(Path.Combine(repo.GitDir, "REVERT_HEAD")).Trim();
            Head = new Commands.QuerySingleCommit(repo.FullPath, headSHA).GetResult() ?? new Models.Commit() { SHA = headSHA };
        }
    }

    public class MergeInProgress : InProgressContext
    {
        public string Current
        {
            get;
        }

        public Models.Commit Source
        {
            get;
        }

        public string SourceName
        {
            get;
        }

        public MergeInProgress(Repository repo)
        {
            Name = "Merge";

            _continueCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Editor = Commands.Command.EditorType.None,
                Args = "-c core.commentChar=\"^\" -c core.commentString=\"±\" merge --continue",
            };

            _abortCmd = new Commands.Command
            {
                WorkingDirectory = repo.FullPath,
                Context = repo.FullPath,
                Args = "merge --abort",
            };

            Current = new Commands.QueryCurrentBranch(repo.FullPath).GetResult();

            var sourceSHA = repo.FileSystem.ReadAllText(Path.Combine(repo.GitDir, "MERGE_HEAD")).Trim();
            Source = new Commands.QuerySingleCommit(repo.FullPath, sourceSHA).GetResult() ?? new Models.Commit() { SHA = sourceSHA };
            SourceName = Source.GetFriendlyName();
        }
    }
}
