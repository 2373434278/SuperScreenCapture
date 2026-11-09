using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using SSC.Platform.Abstractions;

namespace SSC.UI.Platform;

/// <summary>
/// Loads the per-OS style dictionary after Controls.axaml so platform setters win.
/// Must be called by both the real app and the headless test app.
/// </summary>
public static class PlatformStyles
{
    private static readonly Uri MacUri = new("avares://SSC.UI/Styles/Platform.Mac.axaml");
    private static readonly Uri WindowsUri = new("avares://SSC.UI/Styles/Platform.Windows.axaml");

    public static void Apply(Avalonia.Application application, PlatformKind kind)
    {
        PlatformInfo.Current = kind;
        var uri = kind == PlatformKind.Windows ? WindowsUri : MacUri;
        if (application.Styles.OfType<StyleInclude>().Any(include => include.Source == uri))
        {
            return;
        }

        application.Styles.Add(new StyleInclude(uri) { Source = uri });
    }
}
