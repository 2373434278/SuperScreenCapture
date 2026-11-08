using System.Globalization;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Domain.Capture;

namespace SSC.Application.Capture;

public sealed class CaptureFileNamer
{
    private readonly IClock _clock;

    public CaptureFileNamer(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string CreatePath(AppSettings settings, CaptureKind kind, string extension)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        if (string.IsNullOrWhiteSpace(settings.SaveDirectory))
        {
            throw new InvalidOperationException("Save directory is not configured.");
        }

        Directory.CreateDirectory(settings.SaveDirectory);

        var date = _clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var type = kind switch
        {
            CaptureKind.Recording => "recording",
            CaptureKind.AnimatedImage => "gif",
            _ => "screenshot"
        };
        var normalizedExtension = extension.TrimStart('.').ToLowerInvariant();

        for (var sequence = 1; sequence < 10_000; sequence++)
        {
            var name = settings.FileNamePattern
                .Replace("{date}", date, StringComparison.OrdinalIgnoreCase)
                .Replace("{type}", type, StringComparison.OrdinalIgnoreCase)
                .Replace("{n}", sequence.ToString("000", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

            var path = Path.Combine(settings.SaveDirectory, $"{name}.{normalizedExtension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique capture file name.");
    }
}
