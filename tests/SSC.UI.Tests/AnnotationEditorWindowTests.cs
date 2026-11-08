using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Events;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using SSC.Domain.History;
using SSC.Infrastructure.Imaging;
using SSC.Infrastructure.Localization;
using SSC.Platform.Abstractions;
using SSC.UI.Editing;
using Xunit;
using DomainPixelPoint = SSC.Domain.Geometry.PixelPoint;

namespace SSC.UI.Tests;

public sealed class AnnotationEditorWindowTests
{
    [AvaloniaFact]
    public void EditorWindow_RendersToolRailAndCanvas()
    {
        var deps = CreateDependencies();
        var frame = CreateFrame(480, 300);
        var window = new AnnotationEditorWindow(
            CreateResult(frame),
            frame,
            deps.Localizer,
            deps.Renderer,
            deps.Screenshots,
            deps.Clipboard);

        try
        {
            window.Show();
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-annotation-editor.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 1000);
            var toolRail = window.FindControl<StackPanel>("ToolRail");
            Assert.NotNull(toolRail);
            Assert.Equal(11, toolRail!.Children.Count);

            var undoCount = window.FindControl<TextBlock>("UndoCountText");
            Assert.Equal("0", undoCount!.Text);
            Assert.Equal("4", window.FindControl<TextBlock>("WidthValue")!.Text);
            Assert.Equal("24", window.FindControl<TextBlock>("FontValue")!.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Canvas_RendersCommittedAnnotation()
    {
        var deps = CreateDependencies();
        var frame = CreateFrame(480, 300);
        var session = new AnnotationEditorSession(480, 300);
        session.SelectTool(SSC.Domain.Annotations.AnnotationToolKind.Rectangle);
        Assert.True(session.Commit(new AnnotationToolInput(
            new DomainPixelPoint(40, 40),
            new DomainPixelPoint(240, 180))));

        var canvas = new AnnotationEditorCanvas(frame, session, deps.Renderer);
        var host = new Window
        {
            Width = 520,
            Height = 380,
            Content = canvas
        };

        try
        {
            host.Show();
            var rendered = host.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-annotation-canvas.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 500);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public void Canvas_PointerGestureCommitsAnnotation()
    {
        var deps = CreateDependencies();
        var frame = CreateFrame(480, 300);
        var session = new AnnotationEditorSession(480, 300);
        session.SelectTool(SSC.Domain.Annotations.AnnotationToolKind.Rectangle);
        var canvas = new AnnotationEditorCanvas(frame, session, deps.Renderer);
        var host = new Window
        {
            Width = 520,
            Height = 380,
            WindowDecorations = WindowDecorations.None,
            Content = canvas
        };

        try
        {
            host.Show();
            host.MouseDown(new Point(60, 80), MouseButton.Left);
            host.MouseMove(new Point(200, 180));
            host.MouseUp(new Point(200, 180), MouseButton.Left);

            Assert.Single(session.Document.Annotations);
            Assert.Equal(1, session.History.UndoCount);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public void EditorWindow_TextToolCommitsAndUndoRedoUpdatesCounts()
    {
        var deps = CreateDependencies();
        var frame = CreateFrame(480, 300);
        var window = new AnnotationEditorWindow(
            CreateResult(frame),
            frame,
            deps.Localizer,
            deps.Renderer,
            deps.Screenshots,
            deps.Clipboard);

        try
        {
            window.Show();
            var toolRail = window.FindControl<StackPanel>("ToolRail")!;
            ((Button)toolRail.Children[5]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            window.MouseDown(new Point(620, 403), MouseButton.Left);
            var textInput = window.FindControl<TextBox>("TextInputBox")!;
            Assert.True(textInput.IsVisible);

            textInput.Text = "Hello";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);

            Assert.False(textInput.IsVisible);
            Assert.Equal("1", window.FindControl<TextBlock>("UndoCountText")!.Text);

            window.FindControl<Button>("UndoButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("0", window.FindControl<TextBlock>("UndoCountText")!.Text);
            Assert.Equal("1", window.FindControl<TextBlock>("RedoCountText")!.Text);

            window.FindControl<Button>("RedoButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("1", window.FindControl<TextBlock>("UndoCountText")!.Text);
            Assert.Equal("0", window.FindControl<TextBlock>("RedoCountText")!.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PinWindow_IsTopmostAndRenders()
    {
        var deps = CreateDependencies();
        var frame = CreateFrame(240, 160);
        var window = new PinWindow(frame, deps.Localizer);

        try
        {
            window.Show();
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);
        }
        finally
        {
            window.Close();
        }
    }

    private static Dependencies CreateDependencies()
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var paths = new FakeAppPaths();
        var settings = new SettingsService(new InMemorySettingsRepository(), paths, platform);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var clipboard = new StubClipboardService();
        var screenshots = new ScreenshotService(
            new SkiaImageEncoder(),
            clipboard,
            new InMemoryHistoryRepository(),
            settings,
            new CaptureFileNamer(clock),
            clock,
            new InMemoryAppEventBus());
        return new Dependencies(
            new JsonLocalizer(),
            new SkiaAnnotationRenderer(),
            screenshots,
            clipboard);
    }

    private static ScreenshotResult CreateResult(CaptureFrame frame) =>
        new(
            HistoryEntry.Create(
                Guid.NewGuid(),
                CaptureKind.Screenshot,
                "shot",
                "/tmp/ssc-shot.png",
                DateTimeOffset.UtcNow,
                frame.Width,
                frame.Height,
                tags: new[] { "capture" }),
            Array.Empty<byte>(),
            "/tmp/ssc-shot.png");

    private static CaptureFrame CreateFrame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(x * 255 / width);
                pixels[offset + 1] = (byte)(y * 255 / height);
                pixels[offset + 2] = 180;
                pixels[offset + 3] = 255;
            }
        }

        return new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow);
    }

    private sealed record Dependencies(
        JsonLocalizer Localizer,
        SkiaAnnotationRenderer Renderer,
        ScreenshotService Screenshots,
        StubClipboardService Clipboard);

    private sealed class StubClipboardService : IClipboardService
    {
        public CaptureFrame? LastFrame { get; private set; }

        public Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default)
        {
            LastFrame = frame;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryHistoryRepository : IHistoryRepository
    {
        private readonly List<HistoryEntry> _entries = new();

        public IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0) =>
            _entries.Skip(offset).Take(limit).ToArray();

        public HistoryEntry? Get(Guid id) => _entries.FirstOrDefault(entry => entry.Id == id);

        public void Add(HistoryEntry entry) => _entries.Add(entry);

        public void Update(HistoryEntry entry)
        {
        }

        public void Delete(Guid id) => _entries.RemoveAll(entry => entry.Id == id);

        public int Count() => _entries.Count;
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? Read(string key) => _values.GetValueOrDefault(key);

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class FakeAppPaths : IAppPaths
    {
        public string AppDataDirectory => "/tmp/ssc-ui-tests";

        public string LogsDirectory => "/tmp/ssc-ui-tests/logs";

        public string DatabasePath => "/tmp/ssc-ui-tests/ssc.db";

        public string BlobStoreDirectory => "/tmp/ssc-ui-tests/blobs";

        public string ScreenshotsDirectory => "/tmp/ssc-ui-tests/screenshots";

        public string TempDirectory => "/tmp/ssc-ui-tests/tmp";

        public void EnsureCreated()
        {
        }
    }
}
