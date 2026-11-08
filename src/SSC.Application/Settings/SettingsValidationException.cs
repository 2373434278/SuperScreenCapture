namespace SSC.Application.Settings;

public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(string message, IReadOnlyList<HotkeyConflict>? conflicts = null)
        : base(message)
    {
        Conflicts = conflicts ?? Array.Empty<HotkeyConflict>();
    }

    public IReadOnlyList<HotkeyConflict> Conflicts { get; }
}
