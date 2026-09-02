using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

public sealed class DialogCropResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public string? CropPngPath { get; init; }

    public string? CropDumpPath { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public string? Language { get; init; }

    public string? AnchorKind { get; init; }

    public CropRect Crop { get; init; }

    public IReadOnlyList<OcrWord> FullWords { get; init; } = [];

    public IReadOnlyList<OcrWord> CropWords { get; init; } = [];

    public int CropLineCount { get; init; }

    public bool CropHasPlay { get; init; }

    public bool CropHasReport { get; init; }

    public bool CropHasCharacters { get; init; }

    public bool CropHasAdena { get; init; }

    public IReadOnlyList<string> CropLampColors { get; init; } = [];

    public IReadOnlyList<string> FullLampColors { get; init; } = [];

    /// <summary>
    /// Crop pass found the dialog itself (adena + a locate token). Farm-field
    /// parsing is a later step — this only answers "is the panel in the crop?"
    /// </summary>
    public bool DialogContained =>
        Success
        && CropHasAdena
        && (CropHasCharacters || CropHasPlay || CropHasReport);

    public bool LampTableInCrop => Success && CropLampColors.Count > 0;

    public string FrameKind => ImageWidth >= 1600 ? "desktop" : ImageWidth >= 800 ? "framed" : "dialog";
}
