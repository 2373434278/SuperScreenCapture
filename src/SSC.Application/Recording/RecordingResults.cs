using SSC.Domain.History;
using SSC.Domain.Recording;

namespace SSC.Application.Recording;

public sealed record RecordingStatus(
    RecordingState State,
    TimeSpan Elapsed,
    double MicrophoneLevel,
    bool MicrophoneAvailable,
    RecordingOptions? Options,
    string? Error);

public sealed record RecordingStartResult(
    bool Success,
    string Message,
    RecordingOptions? Options,
    string? FilePath);

public sealed record RecordingStopResult(
    bool Success,
    string Message,
    HistoryEntry? Entry,
    TimeSpan Duration);
