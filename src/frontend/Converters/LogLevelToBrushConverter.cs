using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CvAut.Models;

namespace CvAut.Converters
{
    /// <summary>
    /// Maps a <see cref="LogLevel"/> to a design-token brush so log rows are colored by
    /// severity. Resolves the brush from Application resources (single source of truth) —
    /// no hardcoded hex, AOT-safe (no reflection).
    /// </summary>
    public sealed class LogLevelToBrushConverter : IValueConverter
    {
        public static readonly LogLevelToBrushConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var key = value switch
            {
                LogLevel.Error => "AppDangerBrush",
                LogLevel.Warning => "AppWarningBrush",
                LogLevel.Info => "AppTextPrimaryBrush",
                _ => "AppTextMutedBrush",
            };

            if (Application.Current is { } app && app.Resources.TryGetResource(key, app.ActualThemeVariant, out var res) && res is IBrush brush)
            {
                return brush;
            }

            // Fallback colors for safety
            return value switch
            {
                LogLevel.Error => Brushes.Red,
                LogLevel.Warning => Brushes.Orange,
                LogLevel.Info => Brush.Parse("#ABB2BF"),
                _ => Brushes.Gray
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
