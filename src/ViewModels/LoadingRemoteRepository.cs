using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    /// Shown in a launcher page while a remote SSH repository is being connected/opened.
    /// Keeps the UI responsive: the tab is created immediately with this loading state, and
    /// the real <see cref="Repository"/> replaces it once the SSH connection and probes finish.
    /// </summary>
    public class LoadingRemoteRepository : ObservableObject
    {
        public string Host
        {
            get => _host;
            set => SetProperty(ref _host, value);
        }

        public string Path
        {
            get => _path;
            set => SetProperty(ref _path, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        private string _host = string.Empty;
        private string _path = string.Empty;
        private string _message = string.Empty;
    }
}
