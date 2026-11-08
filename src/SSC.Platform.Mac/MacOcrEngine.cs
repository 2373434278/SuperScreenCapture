using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Platform.Mac;

public sealed class MacOcrEngine : IOcrEngine
{
    private static readonly string[] SupportedLanguages = { "zh-Hans", "en-US" };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "macOS Vision";

    public int Priority => 0;

    public bool IsAvailable => OperatingSystem.IsMacOS();

    public IReadOnlyList<string> Languages => SupportedLanguages;

    public Task<OcrResult> RecognizeAsync(
        CaptureFrame frame,
        string? language = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            ArgumentNullException.ThrowIfNull(frame);
            var languages = string.IsNullOrWhiteSpace(language)
                ? string.Join(",", SupportedLanguages)
                : language;

            if (MacNativeMethods.ssc_ocr_image(
                    frame.Pixels,
                    (uint)frame.Width,
                    (uint)frame.Height,
                    (uint)frame.Stride,
                    languages,
                    out var jsonPointer) != 1)
            {
                throw new InvalidOperationException(MacNativeMethods.GetLastError());
            }

            try
            {
                var json = Marshal.PtrToStringUTF8(jsonPointer);
                var payload = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<OcrPayload>(json, JsonOptions);
                if (payload is null)
                {
                    return new OcrResult(string.Empty, Array.Empty<OcrLine>(), languages);
                }

                var lines = payload.Lines
                    .Select(static line => new OcrLine(
                        line.Text ?? string.Empty,
                        new PixelRect(line.X, line.Y, Math.Max(1, line.Width), Math.Max(1, line.Height)),
                        line.Confidence))
                    .ToArray();
                return new OcrResult(payload.Text ?? string.Empty, lines, payload.Language ?? languages);
            }
            finally
            {
                MacNativeMethods.ssc_free_string(jsonPointer);
            }
        }, cancellationToken);

    private sealed record OcrPayload(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("lines")] OcrPayloadLine[] Lines);

    private sealed record OcrPayloadLine(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height,
        [property: JsonPropertyName("confidence")] double Confidence);
}
