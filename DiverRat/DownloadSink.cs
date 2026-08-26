using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace Diver_RaT
{
    internal static class DownloadSink
    {
        private static string? _baseDir;

        public static string DeviceFolder(Window owner, string deviceName)
        {
            if (_baseDir is null)
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Choose base folder for downloaded files",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
                if (dlg.ShowDialog(owner) != true) return "";
                _baseDir = dlg.FolderName;
            }

            var dir = Path.Combine(_baseDir, Sanitize(deviceName));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string CurrentBase => _baseDir ?? "";

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "device" : clean;
        }
    }
}
