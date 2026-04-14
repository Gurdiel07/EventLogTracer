using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EventLogTracer.App.Converters;

public class CorrelationTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string type)
            return new SolidColorBrush(Color.Parse("#6B6F82"));

        return type switch
        {
            "Burst"            => new SolidColorBrush(Color.Parse("#6B6F82")),
            "ErrorCascade"     => new SolidColorBrush(Color.Parse("#D13438")),
            "AuthSequence"     => new SolidColorBrush(Color.Parse("#3b82f6")),
            "ServiceLifecycle" => new SolidColorBrush(Color.Parse("#FF8C00")),
            _                  => new SolidColorBrush(Color.Parse("#6B6F82")),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
