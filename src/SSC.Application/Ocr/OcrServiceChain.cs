using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.Application.Ocr;

public sealed class OcrServiceChain : IOcrService
{
    private readonly IReadOnlyList<IOcrEngine> _engines;

    public OcrServiceChain(IEnumerable<IOcrEngine> engines)
    {
        _engines = (engines ?? throw new ArgumentNullException(nameof(engines)))
            .OrderBy(static engine => engine.Priority)
            .ToArray();
    }

    public IReadOnlyList<string> Languages =>
        _engines
            .Where(static engine => engine.IsAvailable)
            .SelectMany(static engine => engine.Languages)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task<OcrResult> RecognizeAsync(
        CaptureFrame frame,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var errors = new List<string>();
        foreach (var engine in _engines)
        {
            if (!engine.IsAvailable)
            {
                continue;
            }

            try
            {
                return await engine.RecognizeAsync(frame, language, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add($"{engine.Name}: {exception.Message}");
            }
        }

        throw new InvalidOperationException(
            errors.Count == 0
                ? "No OCR engine is available."
                : $"OCR failed: {string.Join("; ", errors)}");
    }
}
