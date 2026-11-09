using SSC.Domain.History;

namespace SSC.Application.Abstractions;

public interface IHistoryRepository
{
    IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0);

    HistoryEntry? Get(Guid id);

    void Add(HistoryEntry entry);

    void Update(HistoryEntry entry);

    void Delete(Guid id);

    void SetFavorite(Guid id, bool isFavorite)
    {
        var entry = Get(id);
        if (entry is not null)
        {
            Update(entry with { IsFavorite = isFavorite });
        }
    }

    void SetTags(Guid id, IEnumerable<string> tags)
    {
        var entry = Get(id);
        if (entry is not null)
        {
            Update(entry with { Tags = HistoryEntry.NormalizeTags(tags) });
        }
    }

    int Count();

    IReadOnlyList<HistoryEntry> Query(HistoryQuery query)
    {
        var normalized = query.Normalize();
        var entries = List(10_000, 0)
            .Where(entry => normalized.Kind is null || entry.Kind == normalized.Kind)
            .Where(entry => normalized.Since is null || entry.CreatedAt >= normalized.Since)
            .Where(entry => normalized.Favorite is null || entry.IsFavorite == normalized.Favorite)
            .Where(entry => normalized.Search is null ||
                            entry.Title.Contains(normalized.Search, StringComparison.OrdinalIgnoreCase) ||
                            entry.OcrText.Contains(normalized.Search, StringComparison.OrdinalIgnoreCase) ||
                            entry.Tags.Any(tag => tag.Contains(normalized.Search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.CreatedAt)
            .Skip(normalized.Offset)
            .Take(normalized.Limit)
            .ToArray();
        return entries;
    }

    int Count(HistoryQuery query)
    {
        var normalized = query.Normalize();
        return Query(normalized with { Limit = 1000, Offset = 0 }).Count;
    }
}
