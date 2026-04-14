using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EventLogTracer.Core.Enums;

namespace EventLogTracer.App.Converters;

/// <summary>
/// Converts <see cref="EventLevel"/> to a background brush.
/// Pass ConverterParameter="badge" for vibrant badge colors;
/// otherwise returns subtle row-tint colors.
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
            EventLevel.Critical    => Brush(badge ? "#E81123" : "#1F1520"),
            EventLevel.Error       => Brush(badge ? "#D13438" : "#1F1218"),
            EventLevel.Warning     => Brush(badge ? "#FF8C00" : "#1F1D12"),
            EventLevel.Information => Brush(badge ? "#3B82F6" : "#121D35"),
            EventLevel.Verbose     => Brush(badge ? "#767676" : "#171A28"),
            _                      => Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
