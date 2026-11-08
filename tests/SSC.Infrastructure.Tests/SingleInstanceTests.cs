using SSC.Infrastructure.SingleInstance;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void TryAcquire_AllowsOnlyOneHolder()
    {
        using var temp = new TestDirectory();
        var lockPath = Path.Combine(temp.Path, "ssc.lock");
        using var first = new FileLockSingleInstanceService(lockPath);
        var second = new FileLockSingleInstanceService(lockPath);

        try
        {
            Assert.True(first.TryAcquire());
            Assert.False(second.TryAcquire());
        }
        finally
        {
            second.Dispose();
        }

        first.Dispose();

        using var third = new FileLockSingleInstanceService(lockPath);
        Assert.True(third.TryAcquire());
    }
}
