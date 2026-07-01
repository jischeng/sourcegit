using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Threading;

namespace SourceGit.Remote
{
    /// <summary>
    /// Owns the live <see cref="RemoteHostSession"/> for every configured remote host and
    /// drives the connect / test / disconnect / reset workflow.
    /// <para>
    /// Sessions are keyed by the ssh host alias and shared across repository tabs, so a host
    /// is connected once (from the settings page) and reused by every repository opened on
    /// it. Connection state lives on the <see cref="Models.RemoteHost"/> model and is updated
    /// on the UI thread so the settings page and the open-repository dropdown stay in sync.
    /// </para>
    /// </summary>
    public sealed class RemoteHostManager
    {
        public static RemoteHostManager Instance { get; } = new();

        /// <summary>Probe connectivity without deploying or launching the server.</summary>
        public async Task TestAsync(Models.RemoteHost host)
        {
            if (host == null || host.IsBusy)
                return;

            SetState(host, Models.RemoteHostState.Testing, "Testing...");

            var session = GetOrCreate(host);
            var (ok, message) = await Task.Run(session.Test).ConfigureAwait(false);

            if (host.State == Models.RemoteHostState.Connected)
                return; // a connect finished while testing; don't clobber it

            SetState(host, ok ? Models.RemoteHostState.Disconnected : Models.RemoteHostState.Failed,
                ok ? "SSH reachable — click Connect to use" : message);
        }

        /// <summary>
        /// Deploy (if needed) and connect. <paramref name="forceRedeploy"/> re-uploads the
        /// server binary even if one is already present (used by Reset).
        /// </summary>
        public async Task<bool> ConnectAsync(Models.RemoteHost host, bool forceRedeploy = false)
        {
            if (host == null)
                return false;

            if (host.IsConnected && !forceRedeploy)
                return true;

            SetState(host, Models.RemoteHostState.Connecting, forceRedeploy ? "Re-deploying..." : "Connecting...");

            var session = GetOrCreate(host);
            try
            {
                await Task.Run(() =>
                {
                    if (forceRedeploy)
                        session.Disconnect();
                    session.Connect(forceRedeploy);
                }).ConfigureAwait(false);

                SetState(host, Models.RemoteHostState.Connected, "Connected");
                return true;
            }
            catch (Exception e)
            {
                session.Disconnect();
                SetState(host, Models.RemoteHostState.Failed, e.Message);
                return false;
            }
        }

        /// <summary>
        /// Disconnect a host and close every repository tab opened on it. Safe to call when
        /// already disconnected.
        /// </summary>
        public void Disconnect(Models.RemoteHost host)
        {
            if (host == null)
                return;

            CloseTabsForHost(host.Host);

            if (_sessions.TryGetValue(Key(host.Host), out var session))
                session.Disconnect();

            SetState(host, Models.RemoteHostState.Disconnected, string.Empty);
        }

        /// <summary>Reset a host: force a fresh deploy and reconnect.</summary>
        public Task<bool> ResetAsync(Models.RemoteHost host)
        {
            CloseTabsForHost(host?.Host);
            return ConnectAsync(host, forceRedeploy: true);
        }

        /// <summary>
        /// Return the connected session for a host, or <c>null</c> when the host is not
        /// currently connected. Used by the repository opener and the remote folder picker so
        /// they only ever work over an already-established connection.
        /// </summary>
        public RemoteHostSession GetConnectedSession(Models.RemoteHost host)
        {
            if (host == null || string.IsNullOrEmpty(host.Host))
                return null;

            return _sessions.TryGetValue(Key(host.Host), out var session) && session.IsConnected ? session : null;
        }

        public RemoteHostSession GetConnectedSession(string host)
        {
            if (string.IsNullOrEmpty(host))
                return null;

            return _sessions.TryGetValue(Key(host), out var session) && session.IsConnected ? session : null;
        }

        private RemoteHostSession GetOrCreate(Models.RemoteHost host)
        {
            var key = Key(host.Host);
            if (!_sessions.TryGetValue(key, out var session))
            {
                session = new RemoteHostSession(host);
                _sessions[key] = session;
            }

            return session;
        }

        private void CloseTabsForHost(string host)
        {
            if (string.IsNullOrEmpty(host))
                return;

            Dispatcher.UIThread.Invoke(() =>
            {
                var launcher = App.GetLauncher();
                if (launcher == null)
                    return;

                var key = Key(host);
                var victims = new List<ViewModels.LauncherPage>();
                foreach (var page in launcher.Pages)
                {
                    if (page.Node is { IsRemote: true, RemoteHost: { } rh } && Key(rh.Host) == key)
                        victims.Add(page);
                }

                foreach (var page in victims)
                    launcher.CloseTab(page);
            });
        }

        private static void SetState(Models.RemoteHost host, Models.RemoteHostState state, string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                host.State = state;
                host.StatusMessage = message;
            });
        }

        private static string Key(string host) => (host ?? string.Empty).Trim();

        private readonly Dictionary<string, RemoteHostSession> _sessions = new();
    }
}
