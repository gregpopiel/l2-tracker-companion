using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Locate + crop + second OCR, with the crop bitmap kept alive for later
/// micro-crops. Caller disposes.
/// </summary>
public sealed class DialogCropRecognition : IDisposable
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public SoftwareBitmap? CropBitmap { get; init; }

    public OcrEngine? Engine { get; init; }

    public CropRect Crop { get; init; }

    public string? AnchorKind { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public IReadOnlyList<OcrWord> FullWords { get; init; } = [];

    public IReadOnlyList<OcrWord> CropWords { get; init; } = [];

    public int CropLineCount { get; init; }

    public void Dispose() => CropBitmap?.Dispose();
}
