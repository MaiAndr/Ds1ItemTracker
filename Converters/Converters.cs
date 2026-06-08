using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Ds1ItemTracker.Converters;

// DisplayState: 0 = unverified/untracked, 1 = tracked/not picked up, 2 = tracked/picked up

[ValueConversion(typeof(int), typeof(Brush))]
public class DisplayStateToBrushConverter : IValueConverter
{
    // Unverified = muted dark gold, NotPickedUp = dark red, PickedUp = dark green
    private static readonly Brush Unverified = new SolidColorBrush(Color.FromRgb(0x28, 0x26, 0x18));
    private static readonly Brush NotPicked  = new SolidColorBrush(Color.FromRgb(0x30, 0x25, 0x25));
    private static readonly Brush Picked     = new SolidColorBrush(Color.FromRgb(0x3A, 0x6B, 0x3A));

    public object Convert(object value, Type t, object p, CultureInfo c) => value is int s
        ? s == 2 ? Picked : s == 1 ? NotPicked : Unverified
        : Unverified;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

[ValueConversion(typeof(int), typeof(Brush))]
public class DisplayStateToForegroundConverter : IValueConverter
{
    private static readonly Brush Unverified = new SolidColorBrush(Color.FromRgb(0x70, 0x68, 0x50));
    private static readonly Brush NotPicked  = new SolidColorBrush(Color.FromRgb(0xB0, 0x98, 0x80));
    private static readonly Brush Picked     = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0x7E));

    public object Convert(object value, Type t, object p, CultureInfo c) => value is int s
        ? s == 2 ? Picked : s == 1 ? NotPicked : Unverified
        : Unverified;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

[ValueConversion(typeof(int), typeof(TextDecorationCollection))]
public class DisplayStateToStrikethroughConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is int s && s == 2 ? TextDecorations.Strikethrough : null!;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

[ValueConversion(typeof(bool), typeof(string))]
public class BoolToConnectedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? "● Connected" : "○ Disconnected";

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

[ValueConversion(typeof(bool), typeof(Brush))]
public class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly Brush Connected    = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0x7E));
    private static readonly Brush Disconnected = new SolidColorBrush(Color.FromRgb(0xC8, 0x5A, 0x5A));

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Connected : Disconnected;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

[ValueConversion(typeof(double), typeof(string))]
public class DoubleToPercentConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is double d ? $"{d:F0}%" : "0%";

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}
