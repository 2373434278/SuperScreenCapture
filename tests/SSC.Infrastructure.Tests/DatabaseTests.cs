using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Infrastructure.Paths;
using SSC.Infrastructure.Persistence;
using SSC.Infrastructure.Time;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public void Migrate_IsIdempotent_AndCreatesSchema()
    {
        using var temp = new TestDirectory();
        var paths = new DefaultAppPaths(temp.Path);
        paths.EnsureCreated();
        var factory = new SqliteConnectionFactory(paths.DatabasePath);
        var migrator = new DatabaseMigrator(factory);

        migrator.Migrate();
        migrator.Migrate();

        using var connection = factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('settings','history_entries','schema_migrations') ORDER BY name;";
        using var reader = command.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(new[] { "history_entries", "schema_migrations", "settings" }, tables);
    }

    [Fact]
    public void SettingsRepository_RoundTripsAndOverwrites()
    {
        using var temp = new TestDirectory();
        var factory = CreateMigratedFactory(temp);
        var repository = new SqliteSettingsRepository(factory, new SystemClock());

        repository.Write("app.settings", "{\"language\":\"zh\"}");
        Assert.Equal("{\"language\":\"zh\"}", repository.Read("app.settings"));

        repository.Write("app.settings", "{\"language\":\"en\"}");
        Assert.Equal("{\"language\":\"en\"}", repository.Read("app.settings"));
        Assert.Null(repository.Read("missing"));
    }

    [Fact]
    public void HistoryRepository_RoundTripsCrud()
    {
        using var temp = new TestDirectory();
        var factory = CreateMigratedFactory(temp);
        var repository = new SqliteHistoryRepository(factory);
        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Screenshot,
            "Design review",
            "/tmp/design.png",
            DateTimeOffset.UtcNow,
            2560,
            1600,
            tags: new[] { "design", "DESIGN", "review" });

        repository.Add(entry);

        Assert.Equal(1, repository.Count());
        var loaded = repository.Get(entry.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Design review", loaded!.Title);
        Assert.Equal(new[] { "design", "review" }, loaded.Tags);
        Assert.Equal(2560, loaded.Width);

        repository.Update(loaded with { Title = "Updated" });
        Assert.Equal("Updated", repository.Get(entry.Id)!.Title);

        var list = repository.List();
        Assert.Single(list);

        repository.Delete(entry.Id);
        Assert.Equal(0, repository.Count());
        Assert.Null(repository.Get(entry.Id));
    }

    [Fact]
    public void HistoryRepository_StoresRecordingDuration()
    {
        using var temp = new TestDirectory();
        var factory = CreateMigratedFactory(temp);
        var repository = new SqliteHistoryRepository(factory);
        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Recording,
            "Sprint demo",
            "/tmp/demo.mp4",
            DateTimeOffset.UtcNow,
            1920,
            1080,
            TimeSpan.FromSeconds(42));

        repository.Add(entry);

        Assert.Equal(TimeSpan.FromSeconds(42), repository.Get(entry.Id)!.Duration);
    }

    [Fact]
    public void HistoryRepository_SearchesTitleTagsAndOcrText()
    {
        using var temp = new TestDirectory();
        var repository = new SqliteHistoryRepository(CreateMigratedFactory(temp));
        var design = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Screenshot,
            "Design review",
            "/tmp/design.png",
            DateTimeOffset.UtcNow,
            2560,
            1600,
            tags: new[] { "design", "tokens" },
            ocrText: "quarterly revenue 中文");
        var demo = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Recording,
            "Sprint demo",
            "/tmp/demo.mp4",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            1920,
            1080,
            TimeSpan.FromSeconds(42),
            tags: new[] { "meeting" });

        repository.Add(design);
        repository.Add(demo);

        Assert.Single(repository.Query(new HistoryQuery(Search: "Design")));
        Assert.Single(repository.Query(new HistoryQuery(Search: "中文")));
        Assert.Single(repository.Query(new HistoryQuery(Search: "meeting")));
        Assert.Single(repository.Query(new HistoryQuery(Kind: CaptureKind.Recording)));
        Assert.Equal(2, repository.Count(new HistoryQuery()));
        Assert.Equal(1, repository.Count(new HistoryQuery(Search: "design")));

        repository.Update(design with { OcrText = "updated keyword" });
        Assert.Single(repository.Query(new HistoryQuery(Search: "updated")));
        Assert.Empty(repository.Query(new HistoryQuery(Search: "quarterly")));

        repository.Delete(design.Id);
        Assert.Empty(repository.Query(new HistoryQuery(Search: "updated")));
    }

    private static SqliteConnectionFactory CreateMigratedFactory(TestDirectory temp)
    {
        var paths = new DefaultAppPaths(temp.Path);
        paths.EnsureCreated();
        var factory = new SqliteConnectionFactory(paths.DatabasePath);
        new DatabaseMigrator(factory).Migrate();
        return factory;
    }
}
