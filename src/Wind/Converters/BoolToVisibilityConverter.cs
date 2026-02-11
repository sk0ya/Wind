using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wind.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        bool invert = parameter?.ToString()?.ToLower() == "invert";

        if (invert) boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString()?.ToLower() == "invert";
        bool result = value is Visibility v && v == Visibility.Visible;

        return invert ? !result : result;
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        bool invert = parameter?.ToString()?.ToLower() == "invert";

        if (invert) isNull = !isNull;

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class AdminStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isAdmin = value is bool b && b;
        return isAdmin ? "現在: 管理者権限で実行中" : "現在: 通常権限で実行中（次回起動時に昇格）";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Windows.Media.Color color)
        {
            return new System.Windows.Media.SolidColorBrush(color);
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Windows.Media.SolidColorBrush brush)
        {
            return brush.Color;
        }
        return System.Windows.Media.Colors.Transparent;
    }
}

public class BackgroundColorPreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return System.Windows.Media.Brushes.Transparent;

        var colorCode = values[0]?.ToString();
        var name = values[1]?.ToString();

        // "Default" shows a gradient pattern to indicate theme default
        if (string.IsNullOrEmpty(colorCode) || name == "Default")
        {
            return new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(60, 60, 60),
                System.Windows.Media.Color.FromRgb(30, 30, 30),
                45);
        }

        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorCode);
            return new System.Windows.Media.SolidColorBrush(color);
        }
        catch
        {
            return System.Windows.Media.Brushes.Transparent;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PathToIconConverter : IValueConverter
{
    private static readonly Dictionary<string, ImageSource?> _iconCache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        return GetIconForPath(path);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    public static ImageSource? GetIconForPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // Normalize path for cache key
        var cacheKey = path.ToLowerInvariant();

        if (_iconCache.TryGetValue(cacheKey, out var cached))
            return cached;

        ImageSource? icon = null;

        try
        {
            // Handle commands in PATH (no extension or not a full path)
            string fullPath = path;
            if (!Path.IsPathRooted(path))
            {
                fullPath = ResolveFromPath(path);
            }

            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                using var sysIcon = Icon.ExtractAssociatedIcon(fullPath);
                if (sysIcon != null)
                {
                    icon = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    icon.Freeze();
                }
            }
        }
        catch
        {
            // Ignore errors, return null
        }

        _iconCache[cacheKey] = icon;
        return icon;
    }

    private static string ResolveFromPath(string command)
    {
        var extensions = new[] { ".exe", ".cmd", ".bat", ".com", "" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, command + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return command;
    }
}
