using Avalonia.Controls.ApplicationLifetimes;
using SSC.Platform.Abstractions;

namespace SSC.UI.Platform;

/// <summary>
/// 关闭与激活行为按平台约定收敛于此，便于在无窗口环境下单测：
/// macOS 关窗永不退出（Dock 点击重开、⌘Q 退出），Windows 由 CloseToTray 决定。
/// </summary>
public static class WindowLifecyclePolicy
{
    public static bool ShouldHideOnClose(PlatformKind platform, bool closeToTray) =>
        platform == PlatformKind.MacOS || closeToTray;

    public static bool ShouldShowOnActivation(ActivationKind kind) =>
        kind == ActivationKind.Reopen;
}
