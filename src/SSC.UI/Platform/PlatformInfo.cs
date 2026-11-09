using SSC.Platform.Abstractions;

namespace SSC.UI.Platform;

/// <summary>
/// Runtime platform used by UI-only decisions (chrome, dialogs, scrollbars).
/// Settable so headless tests can force a platform deterministically.
/// </summary>
public static class PlatformInfo
{
    public static PlatformKind Current { get; set; } =
        OperatingSystem.IsWindows() ? PlatformKind.Windows : PlatformKind.MacOS;

    public static bool IsMacOS => Current == PlatformKind.MacOS;

    public static bool IsWindows => Current == PlatformKind.Windows;
}
