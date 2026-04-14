using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EventLogTracer.App.Converters;

public class CorrelationTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string type)
            return new SolidColorBrush(Color.Parse("#6B7280"));

        return type switch
        {
            "Burst"            => new SolidColorBrush(Color.Parse("#6B7280")),
            "ErrorCascade"     => new SolidColorBrush(Color.Parse("#DC2626")),
            "AuthSequence"     => new SolidColorBrush(Color.Parse("#1160AD")),
            "ServiceLifecycle" => new SolidColorBrush(Color.Parse("#D97706")),
            _                  => new SolidColorBrush(Color.Parse("#6B7280")),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
