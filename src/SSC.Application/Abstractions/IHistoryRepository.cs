using SSC.Domain.History;

namespace SSC.Application.Abstractions;

public interface IHistoryRepository
{
    IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0);

    HistoryEntry? Get(Guid id);

    void Add(HistoryEntry entry);

    void Update(HistoryEntry entry);

    void Delete(Guid id);

    int Count();
}
