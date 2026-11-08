using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;

namespace SSC.Platform.Windows;

public sealed class WindowsOcrEngine : IOcrEngine
{
    public string Name => "Windows OCR";

    public int Priority => 0;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public IReadOnlyList<string> Languages =>
        global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages
            .Select(static language => language.LanguageTag)
            .ToArray();

    public async Task<OcrResult> RecognizeAsync(
        CaptureFrame frame,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var engine = CreateEngine(language)
            ?? throw new InvalidOperationException("Windows OCR is not available for the requested language.");
        var width = frame.Width;
        var height = frame.Height;
        using var bitmap = new global::Windows.Graphics.Imaging.SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            width,
            height,
            global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        using (var bitmapBuffer = bitmap.LockBuffer(global::Windows.Graphics.Imaging.BitmapBufferAccessMode.Write))
        using (var reference = bitmapBuffer.CreateReference())
        {
            var access = WinRT.CastExtensions.As<WindowsGraphicsInterop.IMemoryBufferByteAccess>(reference);
            var result = access.GetBuffer(out var destination, out var capacity);
            if (result < 0)
            {
                throw new InvalidOperationException("Unable to access the OCR bitmap buffer.");
            }

            unsafe
            {
                var copyLength = Math.Min((int)capacity, frame.Pixels.Length);
                fixed (byte* source = frame.Pixels)
                {
                    Buffer.MemoryCopy(source, (void*)destination, capacity, copyLength);
                }
            }
        }

        var recognized = await engine.RecognizeAsync(bitmap);
        var lines = new List<OcrLine>(recognized.Lines.Count);
        foreach (var line in recognized.Lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            var left = line.Words.Min(static word => word.BoundingRect.X);
            var top = line.Words.Min(static word => word.BoundingRect.Y);
            var right = line.Words.Max(static word => word.BoundingRect.X + word.BoundingRect.Width);
            var bottom = line.Words.Max(static word => word.BoundingRect.Y + word.BoundingRect.Height);
            lines.Add(new OcrLine(
                line.Text,
                new PixelRect(
                    (int)Math.Round(left),
                    (int)Math.Round(top),
                    Math.Max(1, (int)Math.Round(right - left)),
                    Math.Max(1, (int)Math.Round(bottom - top))),
                1.0));
        }

        return new OcrResult(
            recognized.Text ?? string.Empty,
            lines,
            engine.RecognizerLanguage.LanguageTag);
    }

    private static global::Windows.Media.Ocr.OcrEngine? CreateEngine(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            var selected = global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(
                new global::Windows.Globalization.Language(language));
            if (selected is not null)
            {
                return selected;
            }
        }

        return global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
    }
}
