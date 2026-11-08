using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Application.Abstractions;

public sealed record OcrLine(string Text, PixelRect Bounds, double Confidence);

public sealed record OcrResult(string Text, IReadOnlyList<OcrLine> Lines, string Language);

public interface IOcrEngine
{
    string Name { get; }

    int Priority { get; }

    bool IsAvailable { get; }

    IReadOnlyList<string> Languages { get; }

    Task<OcrResult> RecognizeAsync(
        CaptureFrame frame,
        string? language = null,
        CancellationToken cancellationToken = default);
}

public interface IOcrService
{
    IReadOnlyList<string> Languages { get; }

    Task<OcrResult> RecognizeAsync(
        CaptureFrame frame,
        string? language = null,
        CancellationToken cancellationToken = default);
}
