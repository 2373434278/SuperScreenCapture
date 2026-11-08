namespace SSC.Domain.Annotations;

public sealed record AnnotationStyle(string Color, int StrokeWidth, int FontSize, int Intensity)
{
    public const string DefaultColor = "#FF4757";

    public static readonly AnnotationStyle Default = new(DefaultColor, 4, 24, 4);

    public AnnotationStyle Normalize() => new(
        string.IsNullOrWhiteSpace(Color) ? DefaultColor : Color,
        Math.Clamp(StrokeWidth, 1, 40),
        Math.Clamp(FontSize, 8, 200),
        Math.Clamp(Intensity, 2, 16));
}
