using SSC.Domain.Capture;
using SSC.Domain.History;
using Xunit;

namespace SSC.Domain.Tests;

public sealed class HistoryEntryFavoriteTests
{
    [Fact]
    public void Create_DefaultsToNotFavorite()
    {
        var entry = HistoryEntry.Create(
            Guid.NewGuid(), CaptureKind.Screenshot, "shot", "/tmp/shot.png", DateTimeOffset.Now, 10, 10);

        Assert.False(entry.IsFavorite);
        Assert.Empty(entry.Tags);
    }

    [Fact]
    public void NormalizeTags_TrimsDeduplicatesAndCaps()
    {
        var tags = Enumerable.Range(0, 20).Select(index => $" tag{index} ").Append("TAG0").Append("");

        var normalized = HistoryEntry.NormalizeTags(tags);

        Assert.Equal(12, normalized.Count);
        Assert.Equal("tag0", normalized[0]);
        Assert.DoesNotContain(normalized, tag => tag != tag.Trim());
    }

    [Fact]
    public void NormalizeTags_DropsOverlongTags()
    {
        var normalized = HistoryEntry.NormalizeTags(new[] { new string('x', 25), "ok" });

        Assert.Equal(new[] { "ok" }, normalized);
    }
}
