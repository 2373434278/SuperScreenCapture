using SSC.Application.Abstractions;
using SSC.Application.Events;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.History;

namespace SSC.Application.Capture;

public sealed class ScreenshotService
{
    private readonly IImageEncoder _encoder;
    private readonly IClipboardService _clipboard;
    private readonly IHistoryRepository _history;
    private readonly SettingsService _settings;
    private readonly CaptureFileNamer _fileNamer;
    private readonly IClock _clock;
    private readonly IAppEventBus _events;

    public ScreenshotService(
        IImageEncoder encoder,
        IClipboardService clipboard,
        IHistoryRepository history,
        SettingsService settings,
        CaptureFileNamer fileNamer,
        IClock clock,
        IAppEventBus events)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _fileNamer = fileNamer ?? throw new ArgumentNullException(nameof(fileNamer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public Task<ScreenshotResult> SaveAsync(
        CaptureFrame frame,
        CaptureKind kind,
        CancellationToken cancellationToken = default) =>
        SaveAsync(frame, kind, format: null, cancellationToken);

    public async Task<ScreenshotResult> SaveAsync(
        CaptureFrame frame,
        CaptureKind kind,
        string? format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var settings = _settings.Current;
        var extension = NormalizeFormat(format ?? settings.DefaultFormat);
        var bytes = await _encoder.EncodeAsync(frame, extension, settings.JpegQuality, cancellationToken)
            .ConfigureAwait(false);

        var path = _fileNamer.CreatePath(settings, kind, extension);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);

        // 自动复制属于用户可在“输出”设置中关闭的副作用。
        if (settings.AutoCopy)
        {
            await _clipboard.CopyImageAsync(frame, cancellationToken).ConfigureAwait(false);
        }

        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            kind,
            Path.GetFileNameWithoutExtension(path),
            path,
            _clock.UtcNow,
            frame.Width,
            frame.Height,
            tags: new[] { "capture" });

        _history.Add(entry);
        _events.Publish(new HistoryChanged(entry.Id));

        return new ScreenshotResult(entry, bytes, path);
    }

    private static string NormalizeFormat(string format) => format?.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "jpg",
        "webp" => "webp",
        _ => "png"
    };
}
