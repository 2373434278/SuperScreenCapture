using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Platform.Mock;

public sealed class MockOcrEngine : IOcrEngine
{
    private static readonly string[] SupportedLanguages = { "zh-Hans", "en-US" };

    public string Name => "Mock OCR";

    public int Priority => 100;

    public bool IsAvailable => true;

    public IReadOnlyList<string> Languages => SupportedLanguages;

    public Task<OcrResult> RecognizeAsync(
        CaptureFrame frame,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var lines = new[]
        {
            new OcrLine("SuperScreenCapture", new PixelRect(32, 32, 220, 26), 0.99),
            new OcrLine("OCR 识别示例", new PixelRect(32, 72, 180, 26), 0.97),
            new OcrLine("M6", new PixelRect(32, 112, 60, 26), 0.95)
        };

        return Task.FromResult(new OcrResult(
            string.Join(Environment.NewLine, lines.Select(static line => line.Text)),
            lines,
            language ?? "zh-Hans"));
    }
}
