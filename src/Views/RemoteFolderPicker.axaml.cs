using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using SourceGit.Remote;

namespace SourceGit.Views
{
    /// <summary>
    /// A lightweight remote folder browser: lists sub-directories of a path on the SSH host
    /// (via `ssh host ls -1p`), lets the user navigate up/into/Go, and returns the chosen
    /// path via <see cref="SelectedPath"/>. Reuses the host alias so ssh config applies.
    /// </summary>
    public partial class RemoteFolderPicker : Window
    {
        public string SelectedPath { get; private set; } = string.Empty;

        private readonly string _host;
        private string _current = ".";
        private readonly ObservableCollection<string> _entries = new();

        public RemoteFolderPicker()
        {
            // for design-time / xaml loader
            InitializeComponent();
        }

        public RemoteFolderPicker(string host, string initialPath)
        {
            _host = host;
            _current = string.IsNullOrEmpty(initialPath) ? "." : initialPath;

            InitializeComponent();


            var txt = this.FindControl<TextBox>("txtPath");
            if (txt != null)
                txt.Text = _current;

            var lst = this.FindControl<ListBox>("lstEntries");
            if (lst != null)
                lst.ItemsSource = _entries;

            _ = LoadAsync();
        }

        public void OnGo(object _, RoutedEventArgs e)
        {
            var txt = this.FindControl<TextBox>("txtPath");
            if (txt != null)
                _current = txt.Text ?? ".";
            _ = LoadAsync();
        }

        public void OnUp(object _, RoutedEventArgs e) => GoUp();

        public void OnHome(object _, RoutedEventArgs e)
        {
            _current = ".";
            UpdatePathText();
            _ = LoadAsync();
        }

        public async void OnDoubleTapped(object _, RoutedEventArgs e)
        {
            var lst = this.FindControl<ListBox>("lstEntries");
            if (lst?.SelectedItem is string sel)
            {
                _current = _current == "." ? sel : (_current == "/" ? "/" + sel : _current + "/" + sel);
                UpdatePathText();
                await LoadAsync();
            }
        }

        public void OnConfirm(object _, RoutedEventArgs e)
        {
            var txt = this.FindControl<TextBox>("txtPath");
            SelectedPath = txt?.Text ?? _current;
            Close(true);
        }

        public void OnCancel(object _, RoutedEventArgs e) => Close(false);

        private void GoUp()
        {
            if (string.IsNullOrEmpty(_current) || _current == "." || _current == "/" )
                return;

            var idx = _current.LastIndexOf('/');
            _current = idx <= 0 ? "/" : _current.Substring(0, idx);
            UpdatePathText();
            _ = LoadAsync();
        }

        private void UpdatePathText()
        {
            var txt = this.FindControl<TextBox>("txtPath");
            if (txt != null)
                txt.Text = _current;
        }

        private async Task LoadAsync()
        {
            _entries.Clear();
            var cmd = $"ls -1p '{_current.Replace("'", "'\\''")}' 2>/dev/null";
            var (stdout, _) = await Task.Run(() => SshExec.Run(_host, cmd)).ConfigureAwait(true);
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.EndsWith('/'))
                    _entries.Add(line.TrimEnd('/'));
            }
        }
    }
}
