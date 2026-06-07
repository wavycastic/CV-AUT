using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace CvAut.WpfApp.Services
{
    public sealed class TemplateImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? templateName = (value as string) ?? (parameter as string);
            if (string.IsNullOrWhiteSpace(templateName))
            {
                return null;
            }

            templateName = NormalizeTemplateName(templateName);
            string templatesRoot = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            using Mat image = TemplateAssetLoader.Load(templatesRoot, templateName, ImreadModes.Unchanged);
            if (image.Empty())
            {
                return null;
            }

            Cv2.ImEncode(".png", image, out byte[] encodedBytes);
            using var stream = new MemoryStream(encodedBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static string NormalizeTemplateName(string templateName)
        {
            const string marker = "/assets/Templates/";
            string normalized = templateName.Replace('\\', '/');
            int markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                normalized = normalized[(markerIndex + marker.Length)..];
            }

            return normalized;
        }
    }
}
