namespace EventLogTracer.Core.Models;

public class SearchValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ParsedTokens { get; set; } = new();
}
