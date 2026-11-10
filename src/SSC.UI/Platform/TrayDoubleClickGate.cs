namespace SSC.UI.Platform;

/// <summary>
/// Avalonia 12 的 TrayIcon 只暴露 <c>Clicked</c>，没有双击事件，因此用点击时间戳
/// 合成双击判定：间隔严格小于阈值视为同一双击，判中后复位以吞掉三连击的后两击。
/// </summary>
public sealed class TrayDoubleClickGate
{
    private readonly long _thresholdMs;
    private long? _lastClickMs;

    public TrayDoubleClickGate(long thresholdMs = 500) => _thresholdMs = thresholdMs;

    /// <summary>记录一次点击；返回 true 表示本次点击与上一次构成双击。</summary>
    public bool RegisterClick(long nowMs)
    {
        if (_lastClickMs is { } last && nowMs - last < _thresholdMs)
        {
            _lastClickMs = null;
            return true;
        }

        _lastClickMs = nowMs;
        return false;
    }
}
