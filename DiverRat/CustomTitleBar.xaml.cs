using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Diver_RaT
{
    public partial class CustomTitleBar : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(CustomTitleBar), new PropertyMetadata(""));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public CustomTitleBar()
        {
            InitializeComponent();
        }

        private Window? OwnerWindow => Window.GetWindow(this);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            var win = OwnerWindow;
            if (win is null) return;

            if (e.ClickCount == 2)
            {
                if (win.ResizeMode != ResizeMode.NoResize && win.ResizeMode != ResizeMode.CanMinimize)
                    win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }

            try { win.DragMove(); } catch { }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (OwnerWindow is { } w) w.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            OwnerWindow?.Close();
        }
    }
}
