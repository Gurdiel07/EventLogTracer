using System.Globalization;
using Avalonia.Data.Converters;
using EventLogTracer.Core.Models;

namespace EventLogTracer.App.Converters;

public class FilterDescriptionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EventFilter filter)
            return string.Empty;

        var parts = new List<string>();

        if (filter.Levels?.Count > 0)
            parts.Add($"Level: {string.Join(", ", filter.Levels)}");

        if (filter.Sources?.Count > 0)
            parts.Add($"Source: {string.Join(", ", filter.Sources)}");

        if (filter.LogNames?.Count > 0)
            parts.Add($"Log: {string.Join(", ", filter.LogNames)}");

        if (filter.EventIds?.Count > 0)
            parts.Add($"EventId: {string.Join(", ", filter.EventIds)}");

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            parts.Add($"Text: \"{filter.SearchText}\"");

        return parts.Count > 0 ? string.Join(" | ", parts) : "All events";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
