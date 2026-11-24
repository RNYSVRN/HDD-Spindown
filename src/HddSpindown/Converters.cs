using System.Globalization;
using System.Windows.Data;

namespace HddSpindown;

/// <summary>Adds the parameter to a number (e.g. height minus the text's ascender space).</summary>
public sealed class OffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value is double d ? d : 0;
        double offset = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 0;
        return Math.Max(0, v + offset);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
