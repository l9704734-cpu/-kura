using System.IO;
using System.Windows.Media.Imaging;

namespace Diver_RaT
{
    public static class ImageHelper
    {
        public static BitmapImage FromBase64(string base64)
        {
            var bytes = System.Convert.FromBase64String(base64);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
