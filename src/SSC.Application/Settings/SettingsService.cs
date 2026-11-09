using System.Text.Json;
using SSC.Application.Abstractions;
using SSC.Platform.Abstractions;

namespace SSC.Application.Settings;

public sealed class SettingsService
{
    public const string StorageKey = "app.settings";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ISettingsRepository _repository;
    private readonly IAppPaths _paths;
    private readonly PlatformDescriptor _platform;
    private AppSettings _current;

    public SettingsService(
        ISettingsRepository repository,
        IAppPaths paths,
        PlatformDescriptor platform)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _current = LoadCore();
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current => _current.Clone();

    public AppSettings CreateDefaults() => new()
    {
        Language = "zh",
        Theme = "dark",
        CloseToTray = true,
        SaveDirectory = _paths.ScreenshotsDirectory,
        Hotkeys = HotkeyCatalog.CreateDefaults(_platform.Kind)
    };

    public AppSettings Load()
    {
        _current = LoadCore();
        return _current.Clone();
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = Normalize(settings.Clone());
        var conflicts = HotkeyConflictDetector.Detect(normalized.Hotkeys);
        if (conflicts.Count > 0)
        {
            throw new SettingsValidationException("Hotkey conflicts detected.", conflicts);
        }

        _repository.Write(StorageKey, JsonSerializer.Serialize(normalized, JsonOptions));
        _current = normalized;
        Changed?.Invoke(this, normalized.Clone());
    }

    private AppSettings LoadCore()
    {
        var defaults = CreateDefaults();
        var raw = _repository.Read(StorageKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaults;
        }

        AppSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return defaults;
        }

        return loaded is null ? defaults : Normalize(loaded);
    }

    private AppSettings Normalize(AppSettings settings)
    {
        var defaults = CreateDefaults();

        settings.SchemaVersion = 2;
        settings.Language = settings.Language is "en" or "zh" ? settings.Language : defaults.Language;
        settings.Theme = settings.Theme is "light" or "dark" ? settings.Theme : defaults.Theme;
        settings.CaptureDelaySeconds = Math.Clamp(settings.CaptureDelaySeconds, 1, 30);
        settings.UpdateChannel = settings.UpdateChannel is "preview" ? "preview" : "stable";
        settings.QuickAccessSeconds = Math.Clamp(settings.QuickAccessSeconds, 2, 15);
        settings.RecentColors = (settings.RecentColors ?? new List<string>())
            .Where(static color => color is { Length: 7 } &&
                                   color[0] == '#' &&
                                   color[1..].All(char.IsAsciiHexDigit))
            .Select(static color => color.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToList();
        settings.GifFps = settings.GifFps is 5 or 10 or 15 ? settings.GifFps : 10;
        settings.GifMaxDurationSeconds = Math.Clamp(settings.GifMaxDurationSeconds, 5, 60);
        settings.GifMaxDimension = settings.GifMaxDimension is 480 or 640 or 960 or 0
            ? settings.GifMaxDimension
            : 960;
        settings.HistoryRetentionDays = settings.HistoryRetentionDays is 0 or 7 or 30 or 90
            ? settings.HistoryRetentionDays
            : 0;
        settings.SaveDirectory = string.IsNullOrWhiteSpace(settings.SaveDirectory)
            ? defaults.SaveDirectory
            : settings.SaveDirectory.Trim();
        settings.FileNamePattern = string.IsNullOrWhiteSpace(settings.FileNamePattern)
            ? defaults.FileNamePattern
            : settings.FileNamePattern.Trim();
        settings.DefaultFormat = settings.DefaultFormat?.Trim().ToLowerInvariant() switch
        {
            "jpg" => "jpg",
            "webp" => "webp",
            _ => "png"
        };
        settings.JpegQuality = Math.Clamp(settings.JpegQuality, 40, 100);
        settings.RecordingFps = settings.RecordingFps is 15 or 30 or 60 ? settings.RecordingFps : 30;
        settings.RecordingBitrateMbps = Math.Clamp(settings.RecordingBitrateMbps, 1, 100);
        settings.RecordingMaxHeight = settings.RecordingMaxHeight is 1080 or 1440 or 2160
            ? settings.RecordingMaxHeight
            : 1080;
        settings.OcrEngine = settings.OcrEngine is "tesseract" ? "tesseract" : "native";
        settings.Upload ??= new UploadSettings();
        settings.Upload.Provider = string.IsNullOrWhiteSpace(settings.Upload.Provider)
            ? "imgur"
            : settings.Upload.Provider.Trim().ToLowerInvariant();
        settings.Upload.Prefix = string.IsNullOrWhiteSpace(settings.Upload.Prefix)
            ? "screenshots"
            : settings.Upload.Prefix.Trim();
        settings.Upload.FieldName = string.IsNullOrWhiteSpace(settings.Upload.FieldName)
            ? "file"
            : settings.Upload.FieldName.Trim();

        var hotkeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in HotkeyCatalog.Actions)
        {
            var gesture = settings.Hotkeys.TryGetValue(action.Key, out var stored)
                && !string.IsNullOrWhiteSpace(stored)
                    ? stored.Trim()
                    : HotkeyCatalog.GetDefaultGesture(action, _platform.Kind);

            hotkeys[action.Key] = gesture;
        }

        settings.Hotkeys = hotkeys;
        return settings;
    }
}
