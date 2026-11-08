using SSC.Domain.Capture;
using SSC.Domain.History;
using Xunit;

namespace SSC.Domain.Tests;

public sealed class HistoryEntryTests
{
    [Fact]
    public void Create_NormalizesTags()
    {
        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Screenshot,
            "  Hello  ",
            "/tmp/file.png",
            DateTimeOffset.UtcNow,
            100,
            50,
            tags: new[] { " design ", "DESIGN", "", "tokens" });

        Assert.Equal("Hello", entry.Title);
        Assert.Equal(new[] { "design", "tokens" }, entry.Tags);
    }

    [Fact]
    public void Create_RejectsMissingTitle()
    {
        Assert.Throws<ArgumentException>(() => HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Screenshot,
            " ",
            "/tmp/file.png",
            DateTimeOffset.UtcNow,
            1,
            1));
    }

    [Fact]
    public void Create_RejectsNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Recording,
            "Recording",
            "/tmp/file.mp4",
            DateTimeOffset.UtcNow,
            1920,
            1080,
            TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Create_KeepsDurationAndDimensions()
    {
        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Recording,
            "Recording",
            "/tmp/file.mp4",
            DateTimeOffset.UtcNow,
            1920,
            1080,
            TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), entry.Duration);
        Assert.Equal(1920, entry.Width);
        Assert.Equal(1080, entry.Height);
    }
}
