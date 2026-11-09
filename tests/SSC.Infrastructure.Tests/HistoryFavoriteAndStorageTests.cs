using SSC.Application.Events;
using SSC.Application.Storage;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Infrastructure.Paths;
using SSC.Infrastructure.Persistence;
using SSC.Infrastructure.Storage;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class HistoryFavoriteAndStorageTests
{
    [Fact]
    public void Repository_PersistsFavoriteAndTags()
    {
        using var temp = new TestDirectory();
        var (paths, factory) = CreateDatabase(temp);
        var repository = new SqliteHistoryRepository(factory);
        var entry = AddEntry(repository, paths, "first", DateTimeOffset.Now, "one");

        repository.SetFavorite(entry.Id, true);
        repository.SetTags(entry.Id, new[] { "Alpha", "alpha", "  ", "Beta" });

        var loaded = repository.Get(entry.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsFavorite);
        Assert.Equal(new[] { "Alpha", "Beta" }, loaded.Tags);

        var favorites = repository.Query(new HistoryQuery(Favorite: true));
        Assert.Single(favorites);
        Assert.Equal(1, repository.Count(new HistoryQuery(Favorite: true)));
    }

    [Fact]
    public async Task Storage_DeleteOlderThan_KeepsFavoritesAndDeletesFiles()
    {
        using var temp = new TestDirectory();
        var (paths, factory) = CreateDatabase(temp);
        var repository = new SqliteHistoryRepository(factory);
        var oldDate = DateTimeOffset.Now.AddDays(-40);

        var oldPlain = AddEntry(repository, paths, "old-plain", oldDate, "old");
        var oldFavorite = AddEntry(repository, paths, "old-favorite", oldDate, "fav");
        var recent = AddEntry(repository, paths, "recent", DateTimeOffset.Now, "new");
        repository.SetFavorite(oldFavorite.Id, true);

        var maintenance = new FileSystemStorageMaintenance(
            repository,
            paths,
            new InMemoryAppEventBus(),
            factory);

        var usageBefore = maintenance.GetUsage();
        Assert.Equal(3, usageBefore.EntryCount);
        Assert.Equal(1, usageBefore.FavoriteCount);
        Assert.True(usageBefore.CaptureBytes > 0);

        var result = await maintenance.DeleteOlderThanAsync(
            DateTimeOffset.Now.AddDays(-7),
            keepFavorites: true);

        Assert.Equal(1, result.DeletedEntries);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Null(repository.Get(oldPlain.Id));
        Assert.NotNull(repository.Get(oldFavorite.Id));
        Assert.NotNull(repository.Get(recent.Id));
        Assert.False(File.Exists(oldPlain.FilePath));
        Assert.True(File.Exists(oldFavorite.FilePath));
    }

    [Fact]
    public async Task Storage_Clear_CanKeepFavorites()
    {
        using var temp = new TestDirectory();
        var (paths, factory) = CreateDatabase(temp);
        var repository = new SqliteHistoryRepository(factory);
        var keep = AddEntry(repository, paths, "keep", DateTimeOffset.Now, "a");
        AddEntry(repository, paths, "drop", DateTimeOffset.Now, "b");
        repository.SetFavorite(keep.Id, true);

        var maintenance = new FileSystemStorageMaintenance(
            repository,
            paths,
            new InMemoryAppEventBus(),
            factory);

        var result = await maintenance.ClearAsync(keepFavorites: true);

        Assert.Equal(1, result.DeletedEntries);
        Assert.Single(repository.List());
        Assert.Equal(keep.Id, repository.List()[0].Id);
    }

    private static (DefaultAppPaths Paths, SqliteConnectionFactory Factory) CreateDatabase(TestDirectory temp)
    {
        var paths = new DefaultAppPaths(temp.Path);
        paths.EnsureCreated();
        var factory = new SqliteConnectionFactory(paths.DatabasePath);
        new DatabaseMigrator(factory).Migrate();
        return (paths, factory);
    }

    private static HistoryEntry AddEntry(
        SqliteHistoryRepository repository,
        DefaultAppPaths paths,
        string name,
        DateTimeOffset createdAt,
        string tag)
    {
        var file = Path.Combine(paths.ScreenshotsDirectory, $"{name}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, new byte[512]);
        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Screenshot,
            name,
            file,
            createdAt,
            100,
            80,
            tags: new[] { tag });
        repository.Add(entry);
        return entry;
    }
}
