using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.History;

namespace SSC.UI.History;

/// <summary>
/// 历史记录卡片的展示模型，负责把领域实体翻译成界面需要的文案与缩略图。
/// </summary>
public sealed class HistoryCardViewModel : IDisposable
{
    public HistoryCardViewModel(HistoryEntry entry, ILocalizer localizer)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        KindLabel = entry.Kind switch
        {
            CaptureKind.Recording => localizer["historyKindRecording"],
            CaptureKind.AnimatedImage => localizer["historyKindGif"],
            _ => localizer["historyKindScreenshot"]
        };
    }

    public HistoryEntry Entry { get; }

    public string Title => Entry.Title;

    public string KindLabel { get; }

    public bool IsRecording => Entry.Kind == CaptureKind.Recording;

    public bool IsAnimated => Entry.Kind == CaptureKind.AnimatedImage;

    public bool IsImage => Entry.Kind is CaptureKind.Screenshot or CaptureKind.AnimatedImage;

    public string Meta
    {
        get
        {
            var local = Entry.CreatedAt.ToLocalTime();
            var size = Entry.Duration is { } duration
                ? $"{FormatDuration(duration)} · {Entry.Width}×{Entry.Height}"
                : $"{Entry.Width}×{Entry.Height}";
            return $"{local:yyyy-MM-dd HH:mm} · {size}";
        }
    }

    /// <summary>详情抽屉中的多行元数据。</summary>
    public string DetailMeta
    {
        get
        {
            var local = Entry.CreatedAt.ToLocalTime();
            var duration = Entry.Duration is { } value ? $" · {FormatDuration(value)}" : string.Empty;
            return $"{KindLabel} · {Entry.Width}×{Entry.Height}{duration}\n{local:yyyy-MM-dd HH:mm:ss}\n{Entry.FilePath}";
        }
    }

    /// <summary>视频时长标签；截图默认隐藏。</summary>
    public string DurationText => Entry.Duration is { } duration ? FormatDuration(duration) : string.Empty;

    public bool HasDuration => Entry.Duration is not null;

    public string ResolutionText => $"{Entry.Width} × {Entry.Height}";

    public string CreatedText => Entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>文件大小，读取失败时返回占位符。</summary>
    public string SizeText
    {
        get
        {
            try
            {
                var length = new FileInfo(Entry.FilePath).Length;
                return length >= 1024 * 1024
                    ? $"{length / 1024d / 1024d:0.0} MB"
                    : length >= 1024
                        ? $"{length / 1024d:0} KB"
                        : $"{length} B";
            }
            catch (Exception)
            {
                return "—";
            }
        }
    }

    /// <summary>历史条目当前只写入 capture 标签。</summary>
    public string TagsText => string.Join(", ", Entry.Tags);

    public Bitmap? Thumbnail { get; set; }

    public bool HasThumbnail => Thumbnail is not null;

    public bool ShowPlaceholder => Thumbnail is null;

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var total = Math.Max(0, (int)duration.TotalSeconds);
        return total >= 3600
            ? $"{total / 3600}:{(total % 3600) / 60:00}:{total % 60:00}"
            : $"{total / 60:00}:{total % 60:00}";
    }
}
