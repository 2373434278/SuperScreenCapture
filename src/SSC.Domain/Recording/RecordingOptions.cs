using SSC.Domain.Geometry;

namespace SSC.Domain.Recording;

public sealed record RecordingOptions(
    string DisplayId,
    PixelRect Region,
    double DisplayScale,
    int Fps,
    int BitrateMbps,
    int MaxHeight,
    bool IncludeMicrophone)
{
    public PixelSize OutputSize
    {
        get
        {
            var width = Math.Max(2, Region.Width);
            var height = Math.Max(2, Region.Height);

            if (MaxHeight > 0 && height > MaxHeight)
            {
                var scale = MaxHeight / (double)height;
                width = Math.Max(2, (int)Math.Round(width * scale));
                height = MaxHeight;
            }

            if ((width & 1) != 0)
            {
                width--;
            }

            if ((height & 1) != 0)
            {
                height--;
            }

            return new PixelSize(Math.Max(2, width), Math.Max(2, height));
        }
    }

    public RecordingOptions Normalize() => this with
    {
        DisplayId = DisplayId?.Trim() ?? string.Empty,
        DisplayScale = DisplayScale > 0 ? DisplayScale : 1,
        Fps = Math.Clamp(Fps, 1, 120),
        BitrateMbps = Math.Clamp(BitrateMbps, 1, 100),
        MaxHeight = MaxHeight <= 0 ? 0 : Math.Clamp(MaxHeight, 240, 4320)
    };
}
