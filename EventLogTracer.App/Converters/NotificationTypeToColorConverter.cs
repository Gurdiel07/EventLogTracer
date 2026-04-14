using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EventLogTracer.Core.Enums;

namespace EventLogTracer.App.Converters;

public class NotificationTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NotificationType type)
            return new SolidColorBrush(Colors.Gray);

        return type switch
        {
            NotificationType.Desktop => new SolidColorBrush(Color.Parse("#3b82f6")),
            NotificationType.Email   => new SolidColorBrush(Color.Parse("#107C10")),
            NotificationType.Webhook => new SolidColorBrush(Color.Parse("#D83B01")),
            _                        => new SolidColorBrush(Colors.Gray)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
