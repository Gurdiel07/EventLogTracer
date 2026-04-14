using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EventLogTracer.Core.Enums;

namespace EventLogTracer.App.Converters;

public class EventLevelToForegroundConverter : IValueConverter
{
    private static readonly IBrush Default = new SolidColorBrush(Color.Parse("#374151"));
    private static readonly IBrush Dimmed  = new SolidColorBrush(Color.Parse("#9CA3AF"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is EventLevel.Verbose ? Dimmed : Default;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
