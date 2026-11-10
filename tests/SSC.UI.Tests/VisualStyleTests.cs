using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SSC.UI.Controls;
using Xunit;

namespace SSC.UI.Tests;

/// <summary>
/// Guards the "de-web" pass over the shared design tokens: primary buttons must
/// paint the flat accent colour instead of the legacy gradient and tooltips must
/// wait for the slower desktop-appropriate delay.
/// </summary>
public sealed class VisualStyleTests
{
    [AvaloniaFact]
    public void PrimaryButton_PaintsSolidAccentInsteadOfGradient()
    {
        var button = new Button
        {
            Content = "Save",
            Classes = { "btn", "btn-primary" }
        };
        var window = new Window { Content = button };
        window.Show();
        try
        {
            Assert.True(
                Avalonia.Application.Current!.TryFindResource(
                    "SscAccent",
                    button.ActualThemeVariant,
                    out var resource),
                "SscAccent token is missing.");
            var accent = Assert.IsType<SolidColorBrush>(resource);

            var background = Assert.IsType<SolidColorBrush>(button.Background);
            Assert.Equal(accent.Color, background.Color);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ToolTip_ShowDelay_IsHalfASecondOnStyledControls()
    {
        var button = new Button { Content = "Hover me" };
        ToolTip.SetTip(button, "Tooltip");
        var toggle = new ToggleButton();
        var input = new TextBox();
        var hotkey = new HotkeyRecorderBox();
        var window = new Window
        {
            Content = new StackPanel { Children = { button, toggle, input, hotkey } }
        };
        window.Show();
        try
        {
            Assert.Equal(500, ToolTip.GetShowDelay(button));
            Assert.Equal(500, ToolTip.GetShowDelay(toggle));
            Assert.Equal(500, ToolTip.GetShowDelay(input));
            Assert.Equal(500, ToolTip.GetShowDelay(hotkey));
        }
        finally
        {
            window.Close();
        }
    }
}
