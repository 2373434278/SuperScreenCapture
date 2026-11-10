using Avalonia.Controls;
using SSC.UI.Platform;

namespace SSC.UI.Dialogs;

/// <summary>
/// 页脚按钮顺序跟随宿主平台约定：macOS 保持取消在左、主按钮在右；
/// Windows 将主按钮移到左侧、取消移到右侧。XAML 里写的是 mac 顺序，
/// 因此只有 Windows 才需要重排，macOS 下不做任何改动。
/// </summary>
public static class DialogButtonOrder
{
    /// <summary>Windows 下把 <paramref name="primary"/> 移到页脚首位，其余按钮不动。</summary>
    public static void Apply(StackPanel actions, Button primary)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(primary);

        if (!PlatformInfo.IsWindows || actions.Children.Count < 2 || !actions.Children.Contains(primary))
        {
            return;
        }

        if (ReferenceEquals(actions.Children[0], primary))
        {
            return;
        }

        actions.Children.Remove(primary);
        actions.Children.Insert(0, primary);
    }

    /// <summary>
    /// Windows 下交换 <paramref name="primary"/> 与 <paramref name="cancel"/> 的列与间距，
    /// 让主按钮落到左侧，同时保持两按钮之间的间距夹在中间。
    /// </summary>
    public static void Apply(Button primary, Button cancel)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(cancel);

        if (!PlatformInfo.IsWindows)
        {
            return;
        }

        var column = Grid.GetColumn(primary);
        Grid.SetColumn(primary, Grid.GetColumn(cancel));
        Grid.SetColumn(cancel, column);

        var margin = primary.Margin;
        primary.Margin = cancel.Margin;
        cancel.Margin = margin;
    }
}
