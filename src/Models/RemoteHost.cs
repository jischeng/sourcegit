using System.Text.Json.Serialization;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    /// <summary>
    /// Lifecycle state of a remote host connection. Drives the status indicator in the
    /// settings page and gates whether the host can be used to open repositories.
    /// </summary>
    public enum RemoteHostState
    {
        Disconnected,
        Testing,
        Connecting,
        Connected,
        Failed,
    }

    /// <summary>
    /// Configuration for a remote host that serves repositories over SSH.
    /// <para>
    /// <see cref="Host"/> is passed straight to <c>ssh</c>/<c>scp</c>, so it should be either
    /// a <c>user@host</c> pair or — preferred — an alias defined in the user's
    /// <c>~/.ssh/config</c>. Reusing the SSH config means ProxyJump (jump hosts / multi-hop),
    /// identity files, agent forwarding, port and passwordless auth are all honored exactly
    /// as the user already configured them.
    /// </para>
    /// <para>
    /// The remote server binary is auto-deployed to <c>~/.sourcegit-server/sourcegit</c> on
    /// the remote host (see <see cref="Remote.RemoteHostSession"/>), so no server path is
    /// configured here.
    /// </para>
    /// <para>
    /// <see cref="Name"/> and <see cref="Host"/> are persisted; the connection
    /// <see cref="State"/> and <see cref="StatusMessage"/> are runtime-only and updated by
    /// <see cref="Remote.RemoteHostManager"/>.
    /// </para>
    /// </summary>
    public class RemoteHost : ObservableObject
    {
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        [JsonPropertyName("host")]
        public string Host
        {
            get => _host;
            set => SetProperty(ref _host, value);
        }

        [JsonIgnore]
        public RemoteHostState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(IsConnected));
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(StatusBrush));
                }
            }
        }

        [JsonIgnore]
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        [JsonIgnore]
        public bool IsConnected => _state == RemoteHostState.Connected;

        [JsonIgnore]
        public bool IsBusy => _state is RemoteHostState.Testing or RemoteHostState.Connecting;

        [JsonIgnore]
        public IBrush StatusBrush => _state switch
        {
            RemoteHostState.Connected => Brushes.ForestGreen,
            RemoteHostState.Connecting => Brushes.Goldenrod,
            RemoteHostState.Testing => Brushes.Goldenrod,
            RemoteHostState.Failed => Brushes.IndianRed,
            _ => Brushes.Gray,
        };

        private string _name = string.Empty;
        private string _host = string.Empty;
        private RemoteHostState _state = RemoteHostState.Disconnected;
        private string _statusMessage = string.Empty;
    }
}
