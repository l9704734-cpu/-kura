using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Diver_RaT
{
    public static class PayloadIcon
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static BitmapSource ToBitmapSource(string path)
        {
            using var bmp = Load(path);
            var hbmp = bmp.GetHbitmap();
            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hbmp, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DeleteObject(hbmp);
            }
        }

        public static bool IsImageFile(string path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
            (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));

        public static Bitmap Load(string path) => new(path);

        public static void WriteIcoFile(string sourcePath, string icoPath)
        {
            var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            using var src = Load(sourcePath);
            var pngs = new List<byte[]>();
            foreach (var sz in sizes)
            {
                using var bmp = Resize(src, sz, sz);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }

            using var fs = File.Create(icoPath);
            using var bw = new BinaryWriter(fs);
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int dim = sizes[i] >= 256 ? 0 : sizes[i];
                bw.Write((byte)dim);
                bw.Write((byte)dim);
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length);
                bw.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) bw.Write(p);
        }

        public static void WriteAndroidMipmaps(string sourcePath, string resDir)
        {
            var targets = new[]
            {
                ("mdpi", 48), ("hdpi", 72), ("xhdpi", 96),
                ("xxhdpi", 144), ("xxxhdpi", 192)
            };
            using var src = Load(sourcePath);
            foreach (var (folder, size) in targets)
            {
                var dir = Path.Combine(resDir, "mipmap-" + folder);
                Directory.CreateDirectory(dir);
                using var bmp = Resize(src, size, size);
                bmp.Save(Path.Combine(dir, "ic_launcher.png"), ImageFormat.Png);
            }
        }

        private static Bitmap Resize(Image src, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, 0, 0, w, h);
            return bmp;
        }
    }
}