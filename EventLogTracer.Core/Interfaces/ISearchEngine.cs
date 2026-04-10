using EventLogTracer.Core.Models;

namespace EventLogTracer.Core.Interfaces;

public interface ISearchEngine
{
    /// <summary>
    /// Filters <paramref name="source"/> using the advanced query syntax.
    /// Returns an empty list (never throws) when the query is malformed.
    /// </summary>
    List<EventEntry> Search(List<EventEntry> source, string query);

    /// <summary>
    /// Validates the query syntax without executing a search.
    /// Never throws — all errors are returned as <see cref="SearchValidationResult.ErrorMessage"/>.
    /// </summary>
    SearchValidationResult Validate(string query);
}
