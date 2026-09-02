using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

public sealed class FarmFieldsResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public string? DumpPath { get; init; }

    public string? XpCropPngPath { get; init; }

    public string? AdenaCropPngPath { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public CropRect DialogCrop { get; init; }

    public string? AnchorKind { get; init; }

    public WordBox? AdenaUnit { get; init; }

    public long? Xp { get; init; }

    public long? Adena { get; init; }

    public long? XpFromTokens { get; init; }

    public long? XpFromCrop { get; init; }

    public long? AdenaFromTokens { get; init; }

    public long? AdenaFromCrop { get; init; }

    public bool UsedAdenaFallback { get; init; }

    public double? Pitch { get; init; }

    public IReadOnlyList<string> XpTokenTexts { get; init; } = [];

    public IReadOnlyList<string> AdenaTokenTexts { get; init; } = [];

    public string FrameKind => ImageWidth >= 1600 ? "desktop" : ImageWidth >= 800 ? "framed" : "dialog";
}
