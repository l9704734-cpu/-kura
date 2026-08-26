using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Diver_RaT
{
    public class FlagImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string code || string.IsNullOrWhiteSpace(code)) return null;

            var uri = new Uri($"pack://application:,,,/Resources/Flags/{code.ToLowerInvariant()}.png", UriKind.Absolute);
            try
            {
                if (Application.GetResourceStream(uri) == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
