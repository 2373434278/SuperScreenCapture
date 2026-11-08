using SSC.Infrastructure.Storage;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class FileBlobStoreTests
{
    [Fact]
    public async Task SaveAsync_IsContentAddressedAndDeduplicates()
    {
        using var temp = new TestDirectory();
        var store = new FileBlobStore(Path.Combine(temp.Path, "blobs"));
        var content = new byte[] { 1, 2, 3, 4, 5 };

        var first = await store.SaveAsync(
            new MemoryStream(content),
            "png",
            TestContext.Current.CancellationToken);
        var second = await store.SaveAsync(
            new MemoryStream(content),
            ".png",
            TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{2}/[0-9a-f]{62}\\.png$", first);
        Assert.True(store.Exists(first));
        Assert.Single(Directory.GetFiles(Path.Combine(temp.Path, "blobs"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Delete_RemovesBlob()
    {
        using var temp = new TestDirectory();
        var store = new FileBlobStore(Path.Combine(temp.Path, "blobs"));
        var relative = await store.SaveAsync(
            new MemoryStream(new byte[] { 9, 9, 9 }),
            "webp",
            TestContext.Current.CancellationToken);

        Assert.True(store.Delete(relative));
        Assert.False(store.Exists(relative));
        Assert.False(store.Delete(relative));
    }

    [Fact]
    public void GetFullPath_RejectsTraversal()
    {
        using var temp = new TestDirectory();
        var store = new FileBlobStore(Path.Combine(temp.Path, "blobs"));

        Assert.Throws<ArgumentException>(() => store.GetFullPath("../escape.png"));
    }

    [Fact]
    public async Task SaveAsync_RejectsInvalidExtension()
    {
        using var temp = new TestDirectory();
        var store = new FileBlobStore(Path.Combine(temp.Path, "blobs"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(
                new MemoryStream(new byte[] { 1 }),
                "../x",
                TestContext.Current.CancellationToken));
    }
}
