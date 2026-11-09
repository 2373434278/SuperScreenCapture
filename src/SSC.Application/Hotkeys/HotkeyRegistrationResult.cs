namespace SSC.Application.Hotkeys;

public enum HotkeyRegistrationError
{
    None = 0,
    InvalidGesture = 1,
    AlreadyInUse = 2,
    Unsupported = 3,
    Unknown = 4
}

public readonly record struct HotkeyRegistrationResult(
    bool Success,
    HotkeyRegistrationError Error,
    string? Detail)
{
    public static HotkeyRegistrationResult Ok => new(true, HotkeyRegistrationError.None, null);

    public static HotkeyRegistrationResult Fail(HotkeyRegistrationError error, string? detail = null) =>
        new(false, error, detail);
}
