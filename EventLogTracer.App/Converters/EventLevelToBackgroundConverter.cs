using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EventLogTracer.Core.Enums;

namespace EventLogTracer.App.Converters;

/// <summary>
/// Converts <see cref="EventLevel"/> to a background brush.
/// Pass ConverterParameter="badge" for vibrant badge colors;
/// otherwise returns subtle row-tint colors for light theme.
/// </summary>
public class EventLevelToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EventLevel level) return Brushes.Transparent;

        bool badge = parameter is string s &&
                     s.Equals("badge", StringComparison.OrdinalIgnoreCase);

        return level switch
        {
            EventLevel.Critical    => Brush(badge ? "#E81123" : "#FEF2F2"),
            EventLevel.Error       => Brush(badge ? "#D13438" : "#FFF1F2"),
            EventLevel.Warning     => Brush(badge ? "#D97706" : "#FFFBEB"),
            EventLevel.Information => Brush(badge ? "#1160AD" : "#EFF6FF"),
            EventLevel.Verbose     => Brush(badge ? "#6B7280" : "#F9FAFB"),
            _                      => Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
