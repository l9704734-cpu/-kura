using System.Windows;
using System.Windows.Input;

namespace Diver_RaT
{
    public partial class InputPromptWindow : Window
    {
        public string Value => ValueBox.Text;

        public InputPromptWindow(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            TitleBar.Title = title;
            PromptText.Text = prompt;
            ValueBox.Text = defaultValue;
            ValueBox.SelectAll();
            Loaded += (_, _) => ValueBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                DialogResult = true;
                Close();
            }
        }
    }
}
