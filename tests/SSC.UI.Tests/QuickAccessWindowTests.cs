using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SSC.Application.Capture;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Infrastructure.Imaging;
using SSC.Infrastructure.Localization;
using SSC.UI.QuickAccess;
using Xunit;

namespace SSC.UI.Tests;

public sealed class QuickAccessWindowTests
{
    [AvaloniaFact]
    public async Task QuickAccess_RendersPreviewAndActions()
    {
        using var temp = new TempDirectory();
        var frame = CreateFrame(320, 200);
        var path = Path.Combine(temp.Path, "quick-access.png");
        var bytes = await new SkiaImageEncoder()
            .EncodeAsync(frame, "png", 90, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);

        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Screenshot,
            Path.GetFileName(path),
            path,
            DateTimeOffset.Now,
            320,
            200);
        var result = new ScreenshotResult(entry, bytes, path);
        var copied = false;
        var discarded = false;

        var window = new QuickAccessWindow(
            result,
            frame,
            new JsonLocalizer(),
            seconds: 10,
            new QuickAccessActions(
                Copy: () => { copied = true; return Task.CompletedTask; },
                Edit: () => { },
                Pin: () => { },
                Upload: () => { },
                Discard: () => discarded = true));

        try
        {
            window.Opacity = 1;
            window.Show();
            await Task.Delay(300, TestContext.Current.CancellationToken);

            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            Assert.True(rendered!.PixelSize.Width >= 300);

            window.Close();
            Assert.False(copied);
            Assert.False(discarded);
        }
        finally
        {
            window.Close();
        }
    }

    private static CaptureFrame CreateFrame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[offset + 2] = 120;
                pixels[offset + 3] = 255;
            }
        }

        return new CaptureFrame(width, height, pixels, DateTimeOffset.Now);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ssc-quick-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
