using SSC.Domain.Capture;

namespace SSC.Domain.History;

public sealed record HistoryQuery(
    string? Search = null,
    CaptureKind? Kind = null,
    DateTimeOffset? Since = null,
    int Limit = 200,
    int Offset = 0,
    bool? Favorite = null)
{
    public HistoryQuery Normalize() => this with
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Limit = Math.Clamp(Limit, 1, 1000),
        Offset = Math.Max(0, Offset)
    };
}
