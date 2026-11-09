using SSC.Domain.Capture;

namespace SSC.Domain.History;

public sealed record HistoryEntry
{
    public required Guid Id { get; init; }

    public required CaptureKind Kind { get; init; }

    public required string Title { get; init; }

    public required string FilePath { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public TimeSpan? Duration { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public bool IsFavorite { get; init; }

    public string OcrText { get; init; } = string.Empty;

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? Array.Empty<string>())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Where(static tag => tag.Length <= 24)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

    public static HistoryEntry Create(
        Guid id,
        CaptureKind kind,
        string title,
        string filePath,
        DateTimeOffset createdAt,
        int width,
        int height,
        TimeSpan? duration = null,
        IEnumerable<string>? tags = null,
        string? ocrText = null,
        bool isFavorite = false)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entry id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var normalizedTags = NormalizeTags(tags);

        return new HistoryEntry
        {
            Id = id,
            Kind = kind,
            Title = title.Trim(),
            FilePath = filePath,
            CreatedAt = createdAt,
            Width = width,
            Height = height,
            Duration = duration,
            Tags = normalizedTags,
            IsFavorite = isFavorite,
            OcrText = ocrText?.Trim() ?? string.Empty
        };
    }
}
