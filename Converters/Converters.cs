using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SaveSpace.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;
            if (Invert) flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v == Visibility.Visible;
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visible = value != null && (value is not string s || !string.IsNullOrWhiteSpace(s));
            if (Invert) visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var count = value is int i ? i : 0;
            var visible = count > 0;
            if (Invert) visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var empty = string.IsNullOrWhiteSpace(value as string);
            if (Invert) empty = !empty;
            return empty ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class FileExistsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value as string ?? "#C8392B")); }
            catch { return new SolidColorBrush(Color.FromRgb(200, 57, 43)); }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class ImageBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ImageSource source)
                return new ImageBrush(source) { Stretch = Stretch.UniformToFill };
            return Binding.DoNothing;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class BoolToVisibilityCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public class NullableLongToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
