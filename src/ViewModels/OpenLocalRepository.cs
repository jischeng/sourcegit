using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace SourceGit.ViewModels
{
    /// <summary>
    /// One entry in the "Open Local Repository" host dropdown. <see cref="Host"/> is
    /// <c>null</c> for the local machine; otherwise it is a connected remote host.
    /// </summary>
    public class RepositoryHostChoice
    {
        public string Display { get; init; } = string.Empty;
        public Models.RemoteHost Host { get; init; }
        public bool IsRemote => Host != null;
    }

    /// <summary>
    /// "Open Local Repository" dialog. A host dropdown at the top selects where the
    /// repository lives: the local machine, or any currently-connected remote host
    /// (connected from Preferences → Remote Hosts). Apart from the machine, the workflow is
    /// identical — folder, group and bookmark — and for remote hosts the folder is chosen
    /// with a browser that runs over the host's existing connection.
    /// </summary>
    public class OpenLocalRepository : Popup
    {
        public List<RepositoryHostChoice> AvailableHosts { get; }

        public RepositoryHostChoice SelectedHost
        {
            get => _selectedHost;
            set
            {
                if (SetProperty(ref _selectedHost, value))
                    OnPropertyChanged(nameof(IsRemote));
            }
        }

        public bool IsRemote => _selectedHost?.IsRemote ?? false;

        public string RepoPath
        {
            get => _repoPath;
            set => SetProperty(ref _repoPath, value);
        }

        public string RemotePath
        {
            get => _remotePath;
            set => SetProperty(ref _remotePath, value);
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

            AvailableHosts = new List<RepositoryHostChoice>
            {
                new RepositoryHostChoice { Display = App.Text("OpenLocalRepository.LocalHost"), Host = null },
            };

            foreach (var host in Preferences.Instance.RemoteHosts)
            {
                if (host.IsConnected)
                    AvailableHosts.Add(new RepositoryHostChoice { Display = string.IsNullOrEmpty(host.Name) ? host.Host : host.Name, Host = host });
            }

            _selectedHost = AvailableHosts[0];

            Groups = new List<RepositoryNode>();
            Groups.Add(new RepositoryNode { Name = "No Group (Uncategorized)", Id = string.Empty });
            Group = group ?? Groups[0];
            CollectGroups(Groups, Preferences.Instance.RepositoryNodes);
        }

        public override async Task<bool> Sure()
        {
            return IsRemote ? await OpenRemoteAsync() : await OpenLocalAsync();
        }

        private async Task<bool> OpenLocalAsync()
        {
            if (string.IsNullOrWhiteSpace(_repoPath) || !Directory.Exists(_repoPath))
            {
                Models.Notification.Send(null, App.Text("OpenLocalRepository.Invalid.LocalPath"), true);
                return false;
            }

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

        private Task<bool> OpenRemoteAsync()
        {
            var host = _selectedHost?.Host;
            if (host == null || !host.IsConnected)
            {
                Models.Notification.Send(null, App.Text("OpenLocalRepository.Invalid.NotConnected"), true);
                return Task.FromResult(false);
            }

            if (string.IsNullOrWhiteSpace(_remotePath))
            {
                Models.Notification.Send(null, App.Text("OpenLocalRepository.Invalid.RemotePath"), true);
                return Task.FromResult(false);
            }

            var parent = _group is { Id: not "" } ? _group : null;
            var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(_remotePath.Trim(), parent, true, save: false);
            node.IsRemote = true;
            node.RemoteHost = host;
            node.Bookmark = _bookmark;

            // Persist after IsRemote/RemoteHost are set so the node reopens as remote next time.
            Preferences.Instance.Save();

            Welcome.Instance.Refresh();
            node.Open();
            return Task.FromResult(true);
        }

        /// <summary>
        /// Open the remote folder browser so the user can navigate directories on the host
        /// (over its existing connection) and pick the repository path.
        /// </summary>
        public void BrowseRemotePath()
        {
            var host = _selectedHost?.Host;
            var session = Remote.RemoteHostManager.Instance.GetConnectedSession(host);
            if (session == null)
            {
                Models.Notification.Send(null, App.Text("OpenLocalRepository.Invalid.NotConnected"), true);
                return;
            }

            var owner = (App.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null)
                return;

            var picker = new Views.RemoteFolderPicker(host, session.Client, _remotePath);
            picker.ShowDialog<bool>(owner).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result && !string.IsNullOrEmpty(picker.SelectedPath))
                    Dispatcher.UIThread.Post(() => RemotePath = picker.SelectedPath);
            });
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
        private string _remotePath = string.Empty;
        private RepositoryHostChoice _selectedHost = null;
        private RepositoryNode _group = null;
        private int _bookmark = 0;
    }
}
