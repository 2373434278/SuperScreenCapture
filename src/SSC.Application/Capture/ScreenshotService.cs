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
        var path = _fileNamer.CreatePath(settings, kind, extension);

        // 常规保存的自动复制由“输出”设置驱动，故把 settings.AutoCopy 交给共享核心。
        return await SaveCoreAsync(frame, kind, path, extension, settings.AutoCopy, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 另存为：写入用户通过系统保存面板选定的路径。与 <see cref="SaveAsync"/> 不同，
    /// 这里绝不触发“自动复制”——用户显式指定落地位置时不应产生额外的剪贴板副作用，
    /// 因此调用共享核心时 autoCopy 恒为 false。目录由保存面板保证存在，不自动创建；写盘失败向上抛出。
    /// </summary>
    public async Task<ScreenshotResult> SaveAsAsync(
        CaptureFrame frame,
        CaptureKind kind,
        string path,
        string? format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return await SaveCoreAsync(frame, kind, path, format, autoCopy: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 保存与另存为的共享核心：归一格式 → 编码 → 写盘 →（可选）自动复制 → 记录历史并广播事件。
    /// 两条路径的产品差异仅由 <paramref name="autoCopy"/> 表达；<paramref name="format"/> 为 null 时
    /// 由 <paramref name="path"/> 的扩展名推断。
    /// </summary>
    private async Task<ScreenshotResult> SaveCoreAsync(
        CaptureFrame frame,
        CaptureKind kind,
        string path,
        string? format,
        bool autoCopy,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var extension = NormalizeFormat(format ?? Path.GetExtension(path));
        var bytes = await _encoder.EncodeAsync(frame, extension, settings.JpegQuality, cancellationToken)
            .ConfigureAwait(false);

        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);

        // 自动复制属于用户可在“输出”设置中关闭的副作用；另存为路径固定传入 false。
        if (autoCopy)
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
