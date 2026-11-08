using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.History;

namespace SSC.UI.History;

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

    public string DetailMeta
    {
        get
        {
            var local = Entry.CreatedAt.ToLocalTime();
            var duration = Entry.Duration is { } value ? $" · {FormatDuration(value)}" : string.Empty;
            return $"{KindLabel} · {Entry.Width}×{Entry.Height}{duration}\n{local:yyyy-MM-dd HH:mm:ss}\n{Entry.FilePath}";
        }
    }

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
