using SSC.Application.Abstractions;
using SSC.Application.Events;
using SSC.Application.Storage;
using SSC.Domain.History;
using SSC.Infrastructure.Persistence;

namespace SSC.Infrastructure.Storage;

public sealed class FileSystemStorageMaintenance : IStorageMaintenance
{
    private readonly IHistoryRepository _history;
    private readonly IAppPaths _paths;
    private readonly IAppEventBus _events;
    private readonly SqliteConnectionFactory _factory;

    public FileSystemStorageMaintenance(
        IHistoryRepository history,
        IAppPaths paths,
        IAppEventBus events,
        SqliteConnectionFactory factory)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public string DataDirectory => _paths.AppDataDirectory;

    public StorageUsage GetUsage()
    {
        var entries = LoadEntries();
        long captureBytes = 0;
        DateTimeOffset? oldest = null;
        var favorites = 0;

        foreach (var entry in entries)
        {
            captureBytes += GetFileLength(entry.FilePath);
            if (entry.IsFavorite)
            {
                favorites++;
            }

            if (oldest is null || entry.CreatedAt < oldest)
            {
                oldest = entry.CreatedAt;
            }
        }

        return new StorageUsage(
            captureBytes,
            GetDatabaseBytes(),
            GetDirectorySize(_paths.BlobStoreDirectory),
            GetDirectorySize(_paths.LogsDirectory),
            entries.Count,
            oldest,
            favorites);
    }

    public Task<CleanupResult> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        bool keepFavorites,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var targets = LoadEntries()
                .Where(entry => entry.CreatedAt < cutoff)
                .Where(entry => !keepFavorites || !entry.IsFavorite)
                .ToArray();
            return DeleteTargets(targets, cancellationToken);
        }, cancellationToken);

    public Task<CleanupResult> ClearAsync(bool keepFavorites, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var targets = LoadEntries()
                .Where(entry => !keepFavorites || !entry.IsFavorite)
                .ToArray();
            var result = DeleteTargets(targets, cancellationToken);
            Vacuum();
            return result;
        }, cancellationToken);

    private CleanupResult DeleteTargets(IReadOnlyList<HistoryEntry> targets, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var skipped = 0;
        long reclaimed = 0;

        foreach (var entry in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = GetFileLength(entry.FilePath);
            try
            {
                if (File.Exists(entry.FilePath))
                {
                    File.Delete(entry.FilePath);
                }

                _history.Delete(entry.Id);
                deleted++;
                reclaimed += length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        if (deleted > 0)
        {
            _events.Publish(new HistoryChanged(Guid.Empty));
        }

        return new CleanupResult(deleted, reclaimed, skipped, null);
    }

    private List<HistoryEntry> LoadEntries()
    {
        var entries = new List<HistoryEntry>();
        const int pageSize = 1000;
        var offset = 0;

        while (true)
        {
            var page = _history.List(pageSize, offset);
            entries.AddRange(page);
            if (page.Count < pageSize)
            {
                return entries;
            }

            offset += pageSize;
        }
    }

    private long GetDatabaseBytes()
    {
        long total = 0;
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            total += GetFileLength(_paths.DatabasePath + suffix);
        }

        return total;
    }

    private void Vacuum()
    {
        try
        {
            using var connection = _factory.Create();
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM;";
            command.ExecuteNonQuery();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Vacuum is best-effort; cleanup itself already succeeded.
        }
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(GetFileLength);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
