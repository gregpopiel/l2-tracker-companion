namespace L2TrackerCompanion.Ocr;

public sealed class OcrWord
{
    public required int LineIndex { get; init; }

    public required int WordIndex { get; init; }

    public required string Text { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }
}
