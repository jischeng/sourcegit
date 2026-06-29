using System;
using System.Text.Json.Nodes;
using System.Threading;
using Avalonia.Threading;

using SourceGit.ViewModels;

namespace SourceGit.Remote
{
    /// <summary>
    /// Subscribes to <c>watch_event</c> notifications from the remote server and refreshes
    /// the repository on the UI thread. Events are debounced (a short quiet period must
    /// pass before refreshing) so a burst of filesystem changes on the remote does not
    /// trigger a refresh storm.
    /// </summary>
    public sealed class RemoteWatcher : IDisposable
    {
        public RemoteWatcher(Repository repo, RpcClient client)
        {
            _repo = repo;
            _client = client;
            _client.NotificationReceived += OnNotification;
            _timer = new Timer(_ => Flush(), null, 500, 500);
        }

        private void OnNotification(string method, JsonNode pars)
        {
            if (method != "watch_event")
                return;

            var path = pars?["path"]?.GetValue<string>();
            if (path == null || !path.Equals(_repo.FullPath, StringComparison.Ordinal))
                return;

            Interlocked.Exchange(ref _dirty, 1);
        }

        private void Flush()
        {
            if (Interlocked.CompareExchange(ref _dirty, 0, 1) == 0)
                return;

            try
            {
                Dispatcher.UIThread.Post(() => _repo.RefreshAll());
            }
            catch
            {
                // Repository may be closing; ignore.
            }
        }

        public void Dispose()
        {
            _client.NotificationReceived -= OnNotification;
            _timer.Dispose();
        }

        private readonly Repository _repo;
        private readonly RpcClient _client;
        private readonly Timer _timer;
        private int _dirty; // 0 = clean, 1 = dirty
    }
}
