using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;

using SourceGit.Remote;

namespace SourceGit.ViewModels
{
    public class OpenLocalRepository : Popup
    {
        [Required(ErrorMessage = "Repository folder is required")]
        [CustomValidation(typeof(OpenLocalRepository), nameof(ValidateRepoPath))]
        public string RepoPath
        {
            get => _repoPath;
            set => SetProperty(ref _repoPath, value, true);
        }

        private bool _isSSHRemote;
        public bool IsSSHRemote
        {
            get => _isSSHRemote;
            set => SetProperty(ref _isSSHRemote, value);
        }

        private string _sshHost = string.Empty;
        public string SSHHost
        {
            get => _sshHost;
            set
            {
                if (SetProperty(ref _sshHost, value))
                    _ = TestConnectionAsync();
            }
        }

        private string _remotePath = string.Empty;
        public string RemotePath
        {
            get => _remotePath;
            set => SetProperty(ref _remotePath, value);
        }

        /// <summary>Host aliases parsed from ~/.ssh/config.</summary>
        public List<string> SshHosts { get; } = SshConfigParser.GetHosts();

        private IBrush _connectionStatusBrush = Brushes.Gray;
        public IBrush ConnectionStatusBrush
        {
            get => _connectionStatusBrush;
            set => SetProperty(ref _connectionStatusBrush, value);
        }

        public List<RepositoryNode> Groups
        {
            get;
        }

        public RepositoryNode Group
        {
            get => _group;
            set => SetProperty(ref _group, value);
        }

        public int Bookmark
        {
            get => _bookmark;
            set => SetProperty(ref _bookmark, value);
        }

        public OpenLocalRepository(string pageId, RepositoryNode group)
        {
            _pageId = pageId;

            Groups = new List<RepositoryNode>();
            Groups.Add(new RepositoryNode { Name = "No Group (Uncategorized)", Id = string.Empty });
            Group = group ?? Groups[0];
            CollectGroups(Groups, Preferences.Instance.RepositoryNodes);
        }

        public static ValidationResult ValidateRepoPath(string folder, ValidationContext _)
        {
            if (!Directory.Exists(folder))
                return new ValidationResult("Given path can NOT be found");
            return ValidationResult.Success;
        }

        public override async Task<bool> Sure()
        {
            if (IsSSHRemote)
                return await OpenRemoteAsync();

            var isBare = await new Commands.IsBareRepository(_repoPath).GetResultAsync();
            var parent = _group is { Id: not "" } ? _group : null;
            var repoRoot = _repoPath;
            if (!isBare)
            {
                var test = await new Commands.QueryRepositoryRootPath(_repoPath).GetResultAsync();
                if (test.IsSuccess && !string.IsNullOrWhiteSpace(test.StdOut))
                {
                    repoRoot = test.StdOut.Trim();
                }
                else
                {
                    var launcher = App.GetLauncher();
                    foreach (var page in launcher.Pages)
                    {
                        if (page.Node.Id.Equals(_pageId, StringComparison.Ordinal))
                        {
                            page.Popup = new Init(page.Node.Id, _repoPath, parent, _bookmark, test.StdErr);
                            break;
                        }
                    }

                    return false;
                }
            }

            var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(repoRoot, parent, true);
            node.Bookmark = _bookmark;
            await node.UpdateStatusAsync(false, null);
            Welcome.Instance.Refresh();
            node.Open();
            return true;
        }

        private async Task<bool> OpenRemoteAsync()
        {
            var host = new Models.RemoteHost
            {
                Name = SSHHost,
                Host = SSHHost,
            };

            var parent = _group is { Id: not "" } ? _group : null;
            var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(RemotePath, parent, true);
            node.IsRemote = true;
            node.RemoteHost = host;
            node.Bookmark = _bookmark;
            await node.UpdateStatusAsync(false, null);
            Welcome.Instance.Refresh();
            node.Open();
            return true;
        }

        /// <summary>
        /// Open the remote folder picker so the user can browse directories on the SSH host
        /// and pick the repository path instead of typing it.
        /// </summary>
        public void BrowseRemotePath()
        {
            if (string.IsNullOrEmpty(SSHHost))
                return;

            var owner = (App.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null)
                return;

            var picker = new Views.RemoteFolderPicker(SSHHost, RemotePath);
            picker.ShowDialog<bool>(owner).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result)
                    Dispatcher.UIThread.Post(() => RemotePath = picker.SelectedPath);
            });
        }

        private async Task TestConnectionAsync()
        {
            if (string.IsNullOrEmpty(SSHHost))
            {
                ConnectionStatusBrush = Brushes.Gray;
                return;
            }

            ConnectionStatusBrush = Brushes.Yellow;
            var host = SSHHost;
            var (stdout, exit) = await Task.Run(() => SshExec.Run(host, "echo OK")).ConfigureAwait(true);
            if (host != SSHHost)
                return; // selection changed meanwhile

            ConnectionStatusBrush = (exit == 0 && stdout.Trim() == "OK") ? Brushes.Green : Brushes.Red;
        }

        private void CollectGroups(List<RepositoryNode> outs, List<RepositoryNode> collections)
        {
            foreach (var node in collections)
            {
                if (!node.IsRepository)
                {
                    outs.Add(node);
                    CollectGroups(outs, node.SubNodes);
                }
            }
        }

        private string _pageId = string.Empty;
        private string _repoPath = string.Empty;
        private RepositoryNode _group = null;
        private int _bookmark = 0;
    }
}
