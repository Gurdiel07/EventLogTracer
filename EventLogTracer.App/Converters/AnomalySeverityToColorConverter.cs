using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EventLogTracer.Core.Enums;

namespace EventLogTracer.App.Converters;

public class AnomalySeverityToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AnomalySeverity severity)
            return new SolidColorBrush(Colors.Gray);

        return severity switch
        {
            AnomalySeverity.Critical => new SolidColorBrush(Color.Parse("#DC2626")),
            AnomalySeverity.High     => new SolidColorBrush(Color.Parse("#EA580C")),
            AnomalySeverity.Medium   => new SolidColorBrush(Color.Parse("#D97706")),
            AnomalySeverity.Low      => new SolidColorBrush(Color.Parse("#1160AD")),
            _                        => new SolidColorBrush(Colors.Gray)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
