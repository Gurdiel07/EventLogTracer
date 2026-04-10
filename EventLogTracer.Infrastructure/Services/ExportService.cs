using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;

namespace EventLogTracer.Infrastructure.Services;

public class ExportService : IExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── CSV (RFC 4180, UTF-8 with BOM) ────────────────────────────────────────

    public async Task ExportToCsvAsync(
        IEnumerable<EventEntry> entries,
        string path,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EventId,Level,Source,LogName,TimeCreated,MachineName,Message,Category,UserId,Keywords,IsBookmarked,Tags");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(',',
                e.EventId,
                e.Level,
                CsvQuote(e.Source),
                CsvQuote(e.LogName),
                e.TimeCreated.ToString("O"),
                CsvQuote(e.MachineName),
                CsvQuote(e.Message.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")),
                CsvQuote(e.Category ?? string.Empty),
                CsvQuote(e.UserId ?? string.Empty),
                CsvQuote(e.Keywords ?? string.Empty),
                e.IsBookmarked,
                CsvQuote(string.Join(';', e.Tags))));
        }

        // UTF-8 with BOM so Excel opens it correctly
        await File.WriteAllTextAsync(path, sb.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
    }

    // ── JSON ─────────────────────────────────────────────────────────────────

    public async Task ExportToJsonAsync(
        IEnumerable<EventEntry> entries,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    public async Task ExportToXmlAsync(
        IEnumerable<EventEntry> entries,
        string path,
        CancellationToken cancellationToken = default)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("EventEntries",
                entries.Select(e =>
                    new XElement("EventEntry",
                        new XElement("Id",           e.Id),
                        new XElement("EventId",      e.EventId),
                        new XElement("Level",        e.Level.ToString()),
                        new XElement("Source",       e.Source),
                        new XElement("LogName",      e.LogName),
                        new XElement("TimeCreated",  e.TimeCreated.ToString("O")),
                        new XElement("MachineName",  e.MachineName),
                        new XElement("Message",      e.Message),
                        new XElement("Category",     e.Category ?? string.Empty),
                        new XElement("UserId",       e.UserId ?? string.Empty),
                        new XElement("Keywords",     e.Keywords ?? string.Empty),
                        new XElement("IsBookmarked", e.IsBookmarked),
                        new XElement("Tags",
                            e.Tags.Select(t => new XElement("Tag", t)))))));

        await Task.Run(() => doc.Save(path), cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Wraps a field in double-quotes and escapes inner quotes (RFC 4180).</summary>
    private static string CsvQuote(string value)
        => $"\"{value.Replace("\"", "\"\"")}\"";
}
