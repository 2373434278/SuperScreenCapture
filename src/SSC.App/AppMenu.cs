using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using SSC.Application.Abstractions;
using SSC.Application.Hotkeys;
using SSC.UI.Platform;

namespace SSC.App;

/// <summary>
/// macOS application menu. Built once (Avalonia.Native cannot replace an exported
/// menu) and relocalized/gesture-updated in place.
/// </summary>
internal sealed class AppMenu
{
    private readonly ILocalizer _localizer;
    private readonly NativeMenu _root = new();
    private readonly List<(NativeMenuItem Item, string Key)> _labels = new();
    private readonly List<(NativeMenuItem Item, string ActionKey)> _bindings = new();

    internal AppMenu(
        TrayActions actions,
        Action hide,
        Action showSettings,
        Action showAbout,
        Action openReadme,
        Action openThirdParty,
        Action checkUpdates,
        ILocalizer localizer)
    {
        _localizer = localizer;

        var application = new NativeMenu();
        Add(application, "appMenuAbout", showAbout);
        Add(application, "appMenuSettings", showSettings, new KeyGesture(Key.OemComma, KeyModifiers.Meta));
        application.Items.Add(new NativeMenuItemSeparator());
        Add(application, "appMenuHide", hide, new KeyGesture(Key.H, KeyModifiers.Meta));
        application.Items.Add(new NativeMenuItemSeparator());
        Add(application, "appMenuQuit", actions.Quit, new KeyGesture(Key.Q, KeyModifiers.Meta));
        _root.Items.Add(new NativeMenuItem { Header = "SSC", Menu = application });

        var file = new NativeMenu();
        Add(file, "trayCapture", actions.CaptureRegion, actionKey: "captureRegion");
        Add(file, "trayCaptureFull", actions.CaptureFull, actionKey: "captureFull");
        Add(file, "trayCaptureWindow", actions.CaptureWindow, actionKey: "captureWindow");
        Add(file, "trayCaptureDelay", actions.CaptureDelayed, actionKey: "captureDelay");
        Add(file, "trayScroll", actions.CaptureScroll);
        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, "trayRecord", actions.StartRecording, actionKey: "record");
        Add(file, "trayRecordFull", actions.StartFullScreenRecording);
        Add(file, "trayGif", actions.StartGif, actionKey: "recordGif");
        Add(file, "trayColorPicker", actions.PickColor, actionKey: "colorPicker");
        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, "trayHistory", actions.ShowHistory, actionKey: "history");
        Add(file, "menuCloseWindow", CloseActiveWindow, new KeyGesture(Key.W, KeyModifiers.Meta));
        AddTopLevel("menuFile", file);

        var edit = new NativeMenu();
        Add(edit, "menuUndo", EditMenuRouter.Undo, new KeyGesture(Key.Z, KeyModifiers.Meta));
        Add(edit, "menuRedo", EditMenuRouter.Redo, new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift));
        edit.Items.Add(new NativeMenuItemSeparator());
        Add(edit, "menuCut", EditMenuRouter.Cut, new KeyGesture(Key.X, KeyModifiers.Meta));
        Add(edit, "menuCopy", EditMenuRouter.Copy, new KeyGesture(Key.C, KeyModifiers.Meta));
        Add(edit, "menuPaste", EditMenuRouter.Paste, new KeyGesture(Key.V, KeyModifiers.Meta));
        Add(edit, "menuSelectAll", EditMenuRouter.SelectAll, new KeyGesture(Key.A, KeyModifiers.Meta));
        AddTopLevel("menuEdit", edit);

        var window = new NativeMenu();
        Add(window, "menuMinimize", MinimizeActiveWindow, new KeyGesture(Key.M, KeyModifiers.Meta));
        Add(window, "menuZoom", ZoomActiveWindow);
        window.Items.Add(new NativeMenuItemSeparator());
        Add(window, "menuMainWindow", actions.ShowWindow);
        Add(window, "trayHistory", actions.ShowHistory);
        AddTopLevel("menuWindow", window);

        var help = new NativeMenu();
        Add(help, "aboutReadme", openReadme);
        Add(help, "aboutThirdParty", openThirdParty);
        help.Items.Add(new NativeMenuItemSeparator());
        Add(help, "aboutCheckUpdate", checkUpdates);
        AddTopLevel("menuHelp", help);
    }

    internal NativeMenu Menu => _root;

    internal void Attach(AvaloniaObject target)
    {
        NativeDock.SetMenu(target, _root);
        try
        {
            NativeMenu.SetMenu(target, _root);
        }
        catch (Exception)
        {
            // NativeMenu.SetMenu is a legacy alias; ignore when unsupported.
        }
    }

    internal void Relocalize()
    {
        foreach (var (item, key) in _labels)
        {
            item.Header = _localizer[key];
        }
    }

    internal void ApplyGestures(IReadOnlyDictionary<string, string> bindings)
    {
        foreach (var (item, actionKey) in _bindings)
        {
            item.Gesture = bindings.TryGetValue(actionKey, out var raw) &&
                           HotkeyGesture.TryParse(raw, out var gesture, out _)
                ? GestureMapper.ToKeyGesture(gesture)
                : null;
        }
    }

    private void AddTopLevel(string headerKey, NativeMenu menu)
    {
        var item = new NativeMenuItem { Header = _localizer[headerKey], Menu = menu };
        _root.Items.Add(item);
        _labels.Add((item, headerKey));
    }

    private NativeMenuItem Add(
        NativeMenu menu,
        string headerKey,
        Action action,
        KeyGesture? gesture = null,
        string? actionKey = null)
    {
        var item = new NativeMenuItem { Header = _localizer[headerKey], Gesture = gesture };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        _labels.Add((item, headerKey));
        if (actionKey is not null)
        {
            _bindings.Add((item, actionKey));
        }

        return item;
    }

    private static Window? ActiveWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.Windows.FirstOrDefault(window => window.IsActive);

    private static void CloseActiveWindow() => ActiveWindow?.Close();

    private static void MinimizeActiveWindow()
    {
        if (ActiveWindow is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private static void ZoomActiveWindow()
    {
        if (ActiveWindow is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
