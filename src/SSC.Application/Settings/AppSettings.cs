namespace SSC.Application.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string Language { get; set; } = "zh";

    public string Theme { get; set; } = "dark";

    public bool LaunchAtLogin { get; set; }

    public bool CloseToTray { get; set; } = true;

    public string SaveDirectory { get; set; } = string.Empty;

    public string FileNamePattern { get; set; } = "{date}-{type}-{n}";

    public string DefaultFormat { get; set; } = "png";

    public int JpegQuality { get; set; } = 92;

    /// <summary>截图完成后是否自动写入剪贴板。</summary>
    public bool AutoCopy { get; set; } = true;

    /// <summary>截图完成后是否自动打开标注编辑器。</summary>
    public bool OpenEditor { get; set; } = true;

    public int RecordingFps { get; set; } = 30;

    public int RecordingBitrateMbps { get; set; } = 12;

    public int RecordingMaxHeight { get; set; } = 1080;

    public bool RecordMicrophone { get; set; } = true;

    public string OcrEngine { get; set; } = "native";

    public bool UploadEnabled { get; set; }

    public UploadSettings Upload { get; set; } = new();

    public Dictionary<string, string> Hotkeys { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.OrdinalIgnoreCase);
        clone.Upload = Upload.Clone();
        return clone;
    }
}
