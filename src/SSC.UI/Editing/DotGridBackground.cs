using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SSC.UI.Editing;

/// <summary>
/// 标注编辑器画布外的点阵底纹，对应原型 P2 的 14px 网格背景。
/// 使用轻量的自定义控件绘制，避免为一次性背景引入图片资源。
/// </summary>
public sealed class DotGridBackground : Control
{
    private const double Spacing = 14;
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.FromArgb(56, 128, 128, 128));

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 以 14px 为步长绘制 1px 圆点，点间距与原型 background-size 保持一致。
        for (var y = Spacing / 2; y < Bounds.Height; y += Spacing)
        {
            for (var x = Spacing / 2; x < Bounds.Width; x += Spacing)
            {
                context.DrawEllipse(DotBrush, null, new Point(x, y), 1, 1);
            }
        }
    }
}
