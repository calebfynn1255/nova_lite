namespace NovaLite.Core.Search;

/// <summary>Search result record returned by <see cref="ISearchProvider"/>.</summary>
public record SearchResult(
    string Title,
    string Snippet,
    string Source,
    float Score);

/// <summary>Stub interface for future semantic/keyword search integration.</summary>
public interface ISearchProvider
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);
}
