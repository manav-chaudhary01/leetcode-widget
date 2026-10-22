using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LeetCodeDesktopWidget.Helpers;

public class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isTrue = value is bool b && b;
        if (Invert) isTrue = !isTrue;
        return isTrue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && (v == Visibility.Visible) ^ Invert;
    }
}
