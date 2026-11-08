using SSC.Domain.History;

namespace SSC.Application.Capture;

public sealed record ScreenshotResult(HistoryEntry Entry, byte[] ImageBytes, string FilePath);
