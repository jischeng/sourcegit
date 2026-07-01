using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using SourceGit.Remote;

namespace SourceGit.Views
{
    /// <summary>One row in the remote folder browser.</summary>
    public class RemoteFolderEntry
    {
        public string Name { get; init; } = string.Empty;
        public bool IsDir { get; init; }
    }

    /// <summary>
    /// Themed remote folder browser. Lists directories on the host over its existing RPC
    /// connection (<c>list_dir</c>), lets the user navigate up / into / type a path, and
    /// returns the chosen folder via <see cref="SelectedPath"/>. Because it reuses the live
    /// connection, navigation is fast and does not re-authenticate per step.
    /// </summary>
    public partial class RemoteFolderPicker : ChromelessWindow
    {
        public string SelectedPath { get; private set; } = string.Empty;

        public RemoteFolderPicker()
        {
            // for design-time / xaml loader
            InitializeComponent();
        }

        public RemoteFolderPicker(Models.RemoteHost host, RpcClient client, string initialPath)
        {
            _host = host;
            _client = client;
            _current = initialPath ?? string.Empty;

            InitializeComponent();

            LstEntries.ItemsSource = _entries;
            _ = LoadAsync(_current);
        }

        private void OnGoUp(object _, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_current) || _current == "/")
                return;

            var idx = _current.TrimEnd('/').LastIndexOf('/');
            var parent = idx <= 0 ? "/" : _current.Substring(0, idx);
            _ = LoadAsync(parent);
            e.Handled = true;
        }

        private void OnGoHome(object _, RoutedEventArgs e)
        {
            _ = LoadAsync("~");
            e.Handled = true;
        }

        private void OnPathKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                _ = LoadAsync(tb.Text ?? string.Empty);
                e.Handled = true;
            }
        }

        private void OnEntryDoubleTapped(object _, RoutedEventArgs e)
        {
            if (LstEntries.SelectedItem is RemoteFolderEntry { IsDir: true } entry)
            {
                var next = _current.TrimEnd('/');
                next = string.IsNullOrEmpty(next) ? "/" + entry.Name : $"{next}/{entry.Name}";
                _ = LoadAsync(next);
            }

            e.Handled = true;
        }

        private void OnConfirm(object _, RoutedEventArgs e)
        {
            // Prefer an explicitly selected sub-directory; otherwise use the current folder.
            if (LstEntries.SelectedItem is RemoteFolderEntry { IsDir: true } entry)
            {
                var sel = _current.TrimEnd('/');
                SelectedPath = string.IsNullOrEmpty(sel) ? "/" + entry.Name : $"{sel}/{entry.Name}";
            }
            else
            {
                SelectedPath = _current;
            }

            Close(true);
        }

        private void OnCancel(object _, RoutedEventArgs e) => Close(false);

        private async Task LoadAsync(string path)
        {
            if (_client == null)
                return;

            LoadingTip.IsVisible = true;

            ListDirResult result = null;
            try
            {
                result = await Task.Run(() =>
                {
                    var node = _client.Call("list_dir", new { path });
                    return node == null ? null : JsonSerializer.Deserialize<ListDirResult>(node.ToJsonString());
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Models.Notification.Send(_host?.Host, $"Failed to list remote folder: {ex.Message}", true);
            }

            LoadingTip.IsVisible = false;

            if (result == null)
                return;

            _current = string.IsNullOrEmpty(result.Path) ? path : result.Path;
            TxtPath.Text = _current;

            _entries.Clear();
            foreach (var entry in result.Entries)
                _entries.Add(new RemoteFolderEntry { Name = entry.Name, IsDir = entry.IsDir });
        }

        private readonly Models.RemoteHost _host;
        private readonly RpcClient _client;
        private string _current = string.Empty;
        private readonly ObservableCollection<RemoteFolderEntry> _entries = new();
    }
}
