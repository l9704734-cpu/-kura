using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Diver_RaT
{
    internal static class IconHelper
    {
        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static readonly Dictionary<string, ImageSource> Cache = new();

        public static ImageSource GetIcon(string name, bool isFolder)
        {
            var key = isFolder ? "<folder>" : "file" + GetExt(name);
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }

            ImageSource? icon = null;
            try
            {
                var shfi = new SHFILEINFO();
                var ext = GetExt(name);
                var probe = isFolder ? "folder" : "file" + (ext == "" ? ".txt" : ext);
                var attrs = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
                SHGetFileInfo(probe, attrs, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

                if (shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        icon = source;
                    }
                    finally
                    {
                        DestroyIcon(shfi.hIcon);
                    }
                }
            }
            catch
            {
                icon = null;
            }

            icon ??= MakeFallback(isFolder);
            lock (Cache) Cache[key] = icon;
            return icon;
        }

        private static string GetExt(string name)
        {
            var i = name.LastIndexOf('.');
            return i < 0 || i == name.Length - 1 ? "" : name[i..].ToLowerInvariant();
        }

        private static ImageSource MakeFallback(bool isFolder)
        {
            var dg = new DrawingGroup();
            if (isFolder)
            {
                dg.Children.Add(new GeometryDrawing(Brush("#D6A94F"), null,
                    Geometry.Parse("M2,4 H10 L12,6 H22 V19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 Z")));
            }
            else
            {
                dg.Children.Add(new GeometryDrawing(Brush("#8AA7FF"), null,
                    Geometry.Parse("M5,2 H14 L19,7 V22 H5 A1,1 0 0 1 4,21 V3 A1,1 0 0 1 5,2 Z")));
                dg.Children.Add(new GeometryDrawing(Brush("#F00A0D09"), null,
                    Geometry.Parse("M14,2 L19,7 H14 Z")));
            }
            var img = new DrawingImage(dg);
            img.Freeze();
            return img;
        }

        private static SolidColorBrush Brush(string hex) =>
            (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
    }
}
