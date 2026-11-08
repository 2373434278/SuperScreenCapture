using SSC.Application.Abstractions;

namespace SSC.UI.Ocr;

/// <summary>
/// OCR 文本行的展示包装：在原始 <see cref="OcrLine"/> 之外补充从 1 开始的行号，
/// 供原型 P6 的文本行列表显示。
/// </summary>
public sealed class OcrLineRow
{
    public OcrLineRow(int index, OcrLine line)
    {
        Index = index;
        Line = line;
    }

    /// <summary>从 0 开始的原始索引，用于和画布选框联动。</summary>
    public int Index { get; }

    public OcrLine Line { get; }

    public string IndexText => (Index + 1).ToString("00");

    public string Text => Line.Text;

    public string ConfidenceText => Line.Confidence.ToString("P0");
}
