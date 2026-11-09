namespace SSC.Application.Storage;

public sealed record StorageUsage(
    long CaptureBytes,
    long DatabaseBytes,
    long BlobBytes,
    long LogBytes,
    int EntryCount,
    DateTimeOffset? OldestEntry,
    int FavoriteCount)
{
    public long TotalBytes => CaptureBytes + DatabaseBytes + BlobBytes + LogBytes;
}

public sealed record CleanupResult(int DeletedEntries, long ReclaimedBytes, int SkippedFiles, string? Error);

public interface IStorageMaintenance
{
    string DataDirectory { get; }

    StorageUsage GetUsage();

    Task<CleanupResult> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        bool keepFavorites,
        CancellationToken cancellationToken = default);

    Task<CleanupResult> ClearAsync(bool keepFavorites, CancellationToken cancellationToken = default);
}
