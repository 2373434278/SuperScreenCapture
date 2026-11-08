using SSC.Domain.Capture;

namespace SSC.Application.Scroll;

public interface IScrollStitcher
{
    CaptureFrame Stitch(IReadOnlyList<CaptureFrame> frames);
}

public sealed class ScrollStitchException : Exception
{
    public ScrollStitchException(string message)
        : base(message)
    {
    }
}

public sealed class ScrollStitcher : IScrollStitcher
{
    private const int MinimumOverlap = 24;
    private const double MaximumDifference = 26;

    public CaptureFrame Stitch(IReadOnlyList<CaptureFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ScrollStitchException("No frames were captured.");
        }

        var current = frames[0];
        for (var index = 1; index < frames.Count; index++)
        {
            current = Append(current, frames[index]);
        }

        return current;
    }

    private static CaptureFrame Append(CaptureFrame top, CaptureFrame bottom)
    {
        var width = Math.Min(top.Width, bottom.Width);
        if (width <= 0)
        {
            throw new ScrollStitchException("Frames have no overlapping width.");
        }

        var minimum = Math.Min(MinimumOverlap, Math.Min(top.Height, bottom.Height) / 2);
        var maximum = Math.Min(top.Height, bottom.Height) - 1;
        if (maximum <= minimum)
        {
            throw new ScrollStitchException("Frames are too small to stitch.");
        }

        var bestOverlap = -1;
        var bestScore = double.MaxValue;
        for (var overlap = maximum; overlap >= minimum; overlap--)
        {
            var score = Difference(top, bottom, width, overlap);
            if (score < bestScore)
            {
                bestScore = score;
                bestOverlap = overlap;
                if (score <= 0.5)
                {
                    break;
                }
            }
        }

        if (bestOverlap < 0 || bestScore > MaximumDifference)
        {
            throw new ScrollStitchException("Unable to find a reliable overlap between frames.");
        }

        if (bestOverlap >= Math.Min(top.Height, bottom.Height) - 2)
        {
            throw new ScrollStitchException("No new content was detected; scroll the page and try again.");
        }

        var appendedRows = bottom.Height - bestOverlap;
        if (appendedRows <= 0)
        {
            throw new ScrollStitchException("No new content was detected; scroll the page and try again.");
        }

        var resultWidth = width;
        var resultHeight = top.Height + appendedRows;
        var pixels = new byte[resultWidth * resultHeight * 4];
        for (var row = 0; row < top.Height; row++)
        {
            Buffer.BlockCopy(
                top.Pixels,
                row * top.Stride,
                pixels,
                row * resultWidth * 4,
                resultWidth * 4);
        }

        for (var row = 0; row < appendedRows; row++)
        {
            Buffer.BlockCopy(
                bottom.Pixels,
                (bestOverlap + row) * bottom.Stride,
                pixels,
                (top.Height + row) * resultWidth * 4,
                resultWidth * 4);
        }

        return new CaptureFrame(resultWidth, resultHeight, pixels, bottom.CapturedAt);
    }

    private static double Difference(CaptureFrame top, CaptureFrame bottom, int width, int overlap)
    {
        var stepX = Math.Max(1, width / 120);
        var stepY = Math.Max(1, overlap / 80);
        var topStart = (top.Height - overlap) * top.Stride;
        var sum = 0.0;
        var count = 0;

        for (var y = 0; y < overlap; y += stepY)
        {
            var topRow = topStart + (y * top.Stride);
            var bottomRow = y * bottom.Stride;
            for (var x = 0; x < width; x += stepX)
            {
                var topOffset = topRow + (x * 4);
                var bottomOffset = bottomRow + (x * 4);
                sum += Math.Abs(top.Pixels[topOffset] - bottom.Pixels[bottomOffset]);
                sum += Math.Abs(top.Pixels[topOffset + 1] - bottom.Pixels[bottomOffset + 1]);
                sum += Math.Abs(top.Pixels[topOffset + 2] - bottom.Pixels[bottomOffset + 2]);
                count += 3;
            }
        }

        return count == 0 ? double.MaxValue : sum / count;
    }
}
