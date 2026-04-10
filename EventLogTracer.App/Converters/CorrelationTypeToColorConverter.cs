using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EventLogTracer.App.Converters;

public class CorrelationTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string type)
            return new SolidColorBrush(Color.Parse("#555555"));

        return type switch
        {
            "Burst"            => new SolidColorBrush(Color.Parse("#767676")),
            "ErrorCascade"     => new SolidColorBrush(Color.Parse("#D13438")),
            "AuthSequence"     => new SolidColorBrush(Color.Parse("#0078D4")),
            "ServiceLifecycle" => new SolidColorBrush(Color.Parse("#FF8C00")),
            _                  => new SolidColorBrush(Color.Parse("#555555")),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
