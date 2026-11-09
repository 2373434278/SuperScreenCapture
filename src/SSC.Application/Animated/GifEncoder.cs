using SSC.Domain.Capture;

namespace SSC.Application.Animated;

/// <summary>
/// 流式 GIF89a 编码器：6×6×6 固定调色板 + 最近邻缩放 + LZW。
/// 每帧只写索引数据，内存占用与帧数无关。
/// </summary>
public sealed class GifEncoder
{
    private const int PaletteLevels = 6;
    private const int PaletteSize = 256;

    private readonly Stream _output;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _palette = new byte[PaletteSize * 3];
    private bool _finished;

    public GifEncoder(Stream output, int width, int height)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame size must be positive.");
        }

        _width = width;
        _height = height;
        BuildPalette();
        WriteHeader();
    }

    public static (int Width, int Height) FitSize(int width, int height, int maxDimension)
    {
        if (maxDimension <= 0 || Math.Max(width, height) <= maxDimension)
        {
            return (Math.Max(2, width & ~1), Math.Max(2, height & ~1));
        }

        var scale = maxDimension / (double)Math.Max(width, height);
        var targetWidth = Math.Max(2, (int)Math.Round(width * scale)) & ~1;
        var targetHeight = Math.Max(2, (int)Math.Round(height * scale)) & ~1;
        return (targetWidth, targetHeight);
    }

    /// <summary>把 BGRA 帧缩放并映射到固定调色板索引。</summary>
    public static byte[] Quantize(CaptureFrame frame, int targetWidth, int targetHeight)
    {
        var indices = new byte[targetWidth * targetHeight];
        var xRatio = frame.Width / (double)targetWidth;
        var yRatio = frame.Height / (double)targetHeight;

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(frame.Height - 1, (int)(y * yRatio));
            var rowOffset = sourceY * frame.Stride;
            var targetRow = y * targetWidth;
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(frame.Width - 1, (int)(x * xRatio));
                var offset = rowOffset + sourceX * 4;
                var blue = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var red = frame.Pixels[offset + 2];

                var r = red * PaletteLevels / 256;
                var g = green * PaletteLevels / 256;
                var b = blue * PaletteLevels / 256;
                indices[targetRow + x] = (byte)((r * PaletteLevels + g) * PaletteLevels + b);
            }
        }

        return indices;
    }

    public void WriteFrame(ReadOnlySpan<byte> indices, int delayCentiseconds)
    {
        if (_finished)
        {
            throw new InvalidOperationException("The GIF has already been finalized.");
        }

        if (indices.Length != _width * _height)
        {
            throw new ArgumentException("Index buffer does not match the frame size.", nameof(indices));
        }

        _output.WriteByte(0x21);
        _output.WriteByte(0xF9);
        _output.WriteByte(0x04);
        _output.WriteByte(0x04);
        WriteUInt16(Math.Max(1, delayCentiseconds));
        _output.WriteByte(0);
        _output.WriteByte(0);

        _output.WriteByte(0x2C);
        WriteUInt16(0);
        WriteUInt16(0);
        WriteUInt16(_width);
        WriteUInt16(_height);
        _output.WriteByte(0);
        _output.WriteByte(0x08);
        GifLzwEncoder.Encode(indices, _output);
    }

    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _output.WriteByte(0x3B);
        _output.Flush();
    }

    private void WriteHeader()
    {
        _output.Write("GIF89a"u8);
        WriteUInt16(_width);
        WriteUInt16(_height);
        _output.WriteByte(0xF7);
        _output.WriteByte(0);
        _output.WriteByte(0);
        _output.Write(_palette);

        _output.WriteByte(0x21);
        _output.WriteByte(0xFF);
        _output.WriteByte(0x0B);
        _output.Write("NETSCAPE2.0"u8);
        _output.WriteByte(0x03);
        _output.WriteByte(0x01);
        WriteUInt16(0);
        _output.WriteByte(0);
    }

    private void BuildPalette()
    {
        var index = 0;
        for (var r = 0; r < PaletteLevels; r++)
        {
            for (var g = 0; g < PaletteLevels; g++)
            {
                for (var b = 0; b < PaletteLevels; b++)
                {
                    _palette[index * 3] = (byte)(r * 255 / (PaletteLevels - 1));
                    _palette[index * 3 + 1] = (byte)(g * 255 / (PaletteLevels - 1));
                    _palette[index * 3 + 2] = (byte)(b * 255 / (PaletteLevels - 1));
                    index++;
                }
            }
        }

        while (index < PaletteSize)
        {
            _palette[index * 3] = 0;
            _palette[index * 3 + 1] = 0;
            _palette[index * 3 + 2] = 0;
            index++;
        }
    }

    private void WriteUInt16(int value)
    {
        _output.WriteByte((byte)(value & 0xFF));
        _output.WriteByte((byte)((value >> 8) & 0xFF));
    }
}

internal static class GifLzwEncoder
{
    public static void Encode(ReadOnlySpan<byte> indices, Stream output)
    {
        const int minimumCodeSize = 8;
        const int clearCode = 1 << minimumCodeSize;
        const int endCode = clearCode + 1;

        var dictionary = new Dictionary<int, int>(4096);
        var codeSize = minimumCodeSize + 1;
        var nextCode = endCode + 1;
        var block = new byte[255];
        var blockCount = 0;
        var bitBuffer = 0;
        var bitCount = 0;

        void FlushBlock()
        {
            if (blockCount == 0)
            {
                return;
            }

            output.WriteByte((byte)blockCount);
            output.Write(block, 0, blockCount);
            blockCount = 0;
        }

        void WriteCode(int code)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                block[blockCount++] = (byte)(bitBuffer & 0xFF);
                bitBuffer >>= 8;
                bitCount -= 8;
                if (blockCount == 255)
                {
                    FlushBlock();
                }
            }
        }

        if (indices.Length == 0)
        {
            WriteCode(clearCode);
            WriteCode(endCode);
        }
        else
        {
            WriteCode(clearCode);
            var prefix = (int)indices[0];
            for (var index = 1; index < indices.Length; index++)
            {
                var suffix = indices[index];
                var key = (prefix << 8) | suffix;
                if (dictionary.TryGetValue(key, out var existing))
                {
                    prefix = existing;
                    continue;
                }

                WriteCode(prefix);
                if (nextCode < 4096)
                {
                    dictionary[key] = nextCode++;
                    if (nextCode > 1 << codeSize && codeSize < 12)
                    {
                        codeSize++;
                    }
                }
                else
                {
                    WriteCode(clearCode);
                    dictionary.Clear();
                    codeSize = minimumCodeSize + 1;
                    nextCode = endCode + 1;
                }

                prefix = suffix;
            }

            WriteCode(prefix);
            WriteCode(endCode);
        }

        while (bitCount > 0)
        {
            block[blockCount++] = (byte)(bitBuffer & 0xFF);
            bitBuffer >>= 8;
            bitCount -= 8;
            if (blockCount == 255)
            {
                FlushBlock();
            }
        }

        FlushBlock();
        output.WriteByte(0);
    }
}
