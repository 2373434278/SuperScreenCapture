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
        // macOS 保留 ⌘⇧3/4/5/6 给系统截图，默认值改用 ⌥⌘ 组合避免冲突。
        new HotkeyAction("captureRegion", "hotkeyCaptureRegion", "⌘⇧2", "Ctrl+Shift+2"),
        new HotkeyAction("captureFull", "hotkeyCaptureFull", "⌥⌘1", "Ctrl+Alt+1"),
        new HotkeyAction("captureWindow", "hotkeyCaptureWindow", "⌥⌘2", "Ctrl+Alt+2"),
        new HotkeyAction("captureDelay", "hotkeyCaptureDelay", "⌥⌘3", "Ctrl+Alt+3"),
        new HotkeyAction("record", "hotkeyRecord", "⌥⌘4", "Ctrl+Alt+4"),
        new HotkeyAction("recordGif", "hotkeyRecordGif", "⌥⌘G", "Ctrl+Alt+G"),
        new HotkeyAction("colorPicker", "hotkeyColorPicker", "⌥⌘P", "Ctrl+Alt+P"),
        new HotkeyAction("history", "hotkeyHistory", "⌥⌘H", "Ctrl+Alt+H"),
        new HotkeyAction("ocr", "hotkeyOcr", "⌥⌘O", "Ctrl+Alt+O")
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
