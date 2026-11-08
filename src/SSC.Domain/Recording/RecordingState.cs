namespace SSC.Domain.Recording;

public enum RecordingState
{
    Idle = 0,
    Preparing = 1,
    Countdown = 2,
    Recording = 3,
    Paused = 4,
    Finalizing = 5,
    Completed = 6,
    Failed = 7
}
