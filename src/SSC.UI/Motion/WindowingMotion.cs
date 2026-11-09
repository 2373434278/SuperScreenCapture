using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;

namespace SSC.UI.Motion;

/// <summary>统一的窗口/面板淡入，系统开启"减少动态效果"时自动跳过。</summary>
public static class WindowingMotion
{
    public static void FadeIn(Window window, int durationMs = 160)
    {
        if (MotionPreferences.IsReduced)
        {
            return;
        }

        window.Opacity = 0;
        window.Opened += (_, _) =>
        {
            Animate(window, 0, 1, durationMs);
            DispatcherTimer.RunOnce(
                () => window.Opacity = 1,
                TimeSpan.FromMilliseconds(durationMs + 250));
        };
    }

    public static void FadeIn(Control control, int durationMs = 140)
    {
        if (MotionPreferences.IsReduced)
        {
            return;
        }

        control.Opacity = 0;
        Animate(control, 0, 1, durationMs);
        DispatcherTimer.RunOnce(
            () => control.Opacity = 1,
            TimeSpan.FromMilliseconds(durationMs + 250));
    }

    private static void Animate(Animatable target, double from, double to, int durationMs)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(Visual.OpacityProperty, from) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, to) }
                }
            }
        };

        _ = animation.RunAsync(target);
    }
}
