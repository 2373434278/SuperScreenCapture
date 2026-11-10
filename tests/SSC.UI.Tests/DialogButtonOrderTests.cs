using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Events;
using SSC.Application.Ocr;
using SSC.Application.Settings;
using SSC.Application.Upload;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Infrastructure.Imaging;
using SSC.Infrastructure.Localization;
using SSC.Platform.Abstractions;
using SSC.Platform.Mock;
using SSC.UI.Dialogs;
using SSC.UI.History;
using SSC.UI.Platform;
using SSC.UI.Upload;
using Xunit;

namespace SSC.UI.Tests;

/// <summary>
/// 页脚按钮顺序必须跟随宿主平台约定：macOS 取消在左、主按钮在右；
/// Windows 主按钮在左、取消在右。
/// </summary>
public sealed class DialogButtonOrderTests
{
    private static readonly Thickness UploadGap = new(8, 0, 0, 0);

    [AvaloniaFact]
    public void PermissionGuide_OnMacOS_KeepsCancelLeft()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.MacOS;
            var window = new PermissionGuideWindow(new MockPermissionService(), new JsonLocalizer());
            var actions = Assert.IsType<StackPanel>(window.FindControl<Button>("LaterButton")!.Parent);
            Assert.Equal(
                new[] { "LaterButton", "OpenSettingsButton" },
                actions.Children.Cast<Control>().Select(control => control.Name).ToArray());
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void PermissionGuide_OnWindows_PutsPrimaryLeft()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;
            var window = new PermissionGuideWindow(new MockPermissionService(), new JsonLocalizer());
            var actions = Assert.IsType<StackPanel>(window.FindControl<Button>("LaterButton")!.Parent);
            Assert.Equal(
                new[] { "OpenSettingsButton", "LaterButton" },
                actions.Children.Cast<Control>().Select(control => control.Name).ToArray());
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void UploadDialog_OnMacOS_KeepsCancelLeft()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.MacOS;
            var window = CreateUploadWindow();
            var cancel = window.FindControl<Button>("CancelButton")!;
            var upload = window.FindControl<Button>("UploadButton")!;
            Assert.Equal(1, Grid.GetColumn(cancel));
            Assert.Equal(2, Grid.GetColumn(upload));
            Assert.Equal(default, cancel.Margin);
            Assert.Equal(UploadGap, upload.Margin);
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void UploadDialog_OnWindows_PutsPrimaryLeft()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;
            var window = CreateUploadWindow();
            var cancel = window.FindControl<Button>("CancelButton")!;
            var upload = window.FindControl<Button>("UploadButton")!;
            Assert.Equal(1, Grid.GetColumn(upload));
            Assert.Equal(2, Grid.GetColumn(cancel));
            // 间距必须继续夹在两按钮中间，即交换后落在取消按钮上。
            Assert.Equal(UploadGap, cancel.Margin);
            Assert.Equal(default, upload.Margin);
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public async Task DeleteModal_OnMacOS_KeepsCancelLeftAndFocused()
    {
        await AssertDeleteFooterAsync(PlatformKind.MacOS);
    }

    [AvaloniaFact]
    public async Task DeleteModal_OnWindows_PutsDangerLeftAndKeepsCancelFocused()
    {
        await AssertDeleteFooterAsync(PlatformKind.Windows);
    }

    private static async Task AssertDeleteFooterAsync(PlatformKind platform)
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = platform;
            var repository = new InMemoryHistoryRepository();
            repository.Add(HistoryEntry.Create(
                Guid.NewGuid(),
                CaptureKind.Screenshot,
                "Delete me",
                Path.Combine(Path.GetTempPath(), "ssc-dialog-order-missing.png"),
                DateTimeOffset.UtcNow,
                320,
                200));
            var window = CreateHistoryWindow(repository);
            try
            {
                window.Show();
                await Task.Delay(500, TestContext.Current.CancellationToken);

                window.FindControl<ListBox>("GridResults")!.SelectedIndex = 0;
                window.FindControl<Button>("DetailDelete")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var cancel = window.FindControl<Button>("DeleteCancel")!;
                var confirm = window.FindControl<Button>("DeleteConfirm")!;
                var actions = Assert.IsType<StackPanel>(cancel.Parent);
                var expected = platform == PlatformKind.Windows
                    ? new[] { "DeleteConfirm", "DeleteCancel" }
                    : new[] { "DeleteCancel", "DeleteConfirm" };
                Assert.Equal(
                    expected,
                    actions.Children.Cast<Control>().Select(control => control.Name).ToArray());

                // 重排后首个按钮不再是取消，初始焦点仍必须落在取消上。
                Assert.True(cancel.IsFocused);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    private static UploadDialogWindow CreateUploadWindow()
    {
        var localizer = new JsonLocalizer();
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(new InMemorySettingsRepository(), new TestPaths(), platform);
        var pipeline = new UploadPipeline(new IUploadProvider[] { new FakeUploadProvider() }, settings, localizer);
        return new UploadDialogWindow(new byte[] { 1, 2, 3 }, "screenshot.png", pipeline, settings, localizer);
    }

    private static HistoryWindow CreateHistoryWindow(IHistoryRepository repository)
    {
        var localizer = new JsonLocalizer();
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(new InMemorySettingsRepository(), new TestPaths(), platform);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var clipboard = new StubClipboardService();
        var screenshots = new ScreenshotService(
            new SkiaImageEncoder(),
            clipboard,
            repository,
            settings,
            new CaptureFileNamer(clock),
            clock,
            new InMemoryAppEventBus());
        return new HistoryWindow(
            repository,
            localizer,
            new SkiaCaptureFrameLoader(),
            new SkiaAnnotationRenderer(),
            screenshots,
            clipboard,
            new OcrServiceChain(new IOcrEngine[] { new MockOcrEngine() }),
            new InMemoryAppEventBus());
    }

    private sealed class FakeUploadProvider : IUploadProvider
    {
        public UploadProviderInfo Info { get; } = new("imgur", "uploadProviderImgur");

        public bool IsConfigured(UploadSettings settings) => true;

        public Task<UploadResult> UploadAsync(
            UploadRequest request,
            UploadSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadResult(Info.Id, "https://example.test/image.png"));

        public Task<bool> DeleteAsync(
            UploadResult result,
            UploadSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class InMemoryHistoryRepository : IHistoryRepository
    {
        private readonly List<HistoryEntry> _entries = new();

        public IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0) =>
            _entries.OrderByDescending(static entry => entry.CreatedAt).Skip(offset).Take(limit).ToArray();

        public HistoryEntry? Get(Guid id) => _entries.FirstOrDefault(entry => entry.Id == id);

        public void Add(HistoryEntry entry) => _entries.Add(entry);

        public void Update(HistoryEntry entry)
        {
            var index = _entries.FindIndex(candidate => candidate.Id == entry.Id);
            if (index >= 0)
            {
                _entries[index] = entry;
            }
        }

        public void Delete(Guid id) => _entries.RemoveAll(entry => entry.Id == id);

        public int Count() => _entries.Count;
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

    private sealed class TestPaths : IAppPaths
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "ssc-dialog-order-tests",
            Guid.NewGuid().ToString("N"));

        public string AppDataDirectory => _root;

        public string LogsDirectory => Path.Combine(_root, "logs");

        public string DatabasePath => Path.Combine(_root, "ssc.db");

        public string BlobStoreDirectory => Path.Combine(_root, "blobs");

        public string ScreenshotsDirectory => _root;

        public string TempDirectory => Path.Combine(_root, "tmp");

        public void EnsureCreated() => Directory.CreateDirectory(_root);
    }
}
