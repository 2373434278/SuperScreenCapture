namespace SSC.Application.Hotkeys;

/// <summary>
/// 平台回调 → 应用命令的桥。App 组合根注册处理器，
/// 平台项目与 Application 均不直接依赖 Avalonia。
/// </summary>
public sealed class HotkeyRouter
{
    public Action<string>? Handler { get; set; }

    public void Dispatch(string actionKey)
    {
        try
        {
            Handler?.Invoke(actionKey);
        }
        catch
        {
            // A global hotkey must never take down the platform callback loop.
        }
    }
}
