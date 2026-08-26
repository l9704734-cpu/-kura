using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Diver_RaT
{
    public partial class EducationalNoticeWindow : Window
    {
        private const int CountdownSeconds = 10;
        private int _remaining = CountdownSeconds;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

        public EducationalNoticeWindow()
        {
            InitializeComponent();
            UpdateCountdownText();
            _timer.Tick += (_, _) =>
            {
                _remaining--;
                if (_remaining <= 0)
                {
                    _timer.Stop();
                    Close();
                    return;
                }
                UpdateCountdownText();
            };
            _timer.Start();
        }

        private void UpdateCountdownText()
        {
            CountdownText.Text = $"This notice will close automatically in {_remaining} second{(_remaining == 1 ? "" : "s")}.";
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _timer.Stop();
                Close();
            }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
