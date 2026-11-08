using SSC.Platform.Abstractions;

namespace SSC.Application.Settings;

public sealed record HotkeyAction(
    string Key,
    string LabelKey,
    string MacGesture,
    string WindowsGesture);

public static class HotkeyCatalog
{
    public static IReadOnlyList<HotkeyAction> Actions { get; } = new[]
    {
        new HotkeyAction("captureRegion", "hotkeyCaptureRegion", "⌘⇧2", "Ctrl+Shift+2"),
        new HotkeyAction("captureFull", "hotkeyCaptureFull", "⌘⇧3", "Ctrl+Shift+3"),
        new HotkeyAction("captureWindow", "hotkeyCaptureWindow", "⌘⇧4", "Ctrl+Shift+4"),
        new HotkeyAction("record", "hotkeyRecord", "⌘⇧6", "Ctrl+Shift+6"),
        new HotkeyAction("history", "hotkeyHistory", "⌘⇧H", "Ctrl+Shift+H"),
        new HotkeyAction("ocr", "hotkeyOcr", "⌘⇧O", "Ctrl+Shift+O")
    };

    public static Dictionary<string, string> CreateDefaults(PlatformKind platform)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in Actions)
        {
            result[action.Key] = GetDefaultGesture(action, platform);
        }

        return result;
    }

    public static string GetDefaultGesture(HotkeyAction action, PlatformKind platform) =>
        platform == PlatformKind.Windows ? action.WindowsGesture : action.MacGesture;
}
