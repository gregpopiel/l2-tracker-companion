using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

public sealed class PlayTimeResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public string? DumpPath { get; init; }

    public string? CropPngPath { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public CropRect DialogCrop { get; init; }

    public string? AnchorKind { get; init; }

    public WordBox? TimeAnchor { get; init; }

    public int? Minutes { get; init; }

    public int? FromTokens { get; init; }

    public int? FromCrop { get; init; }

    public bool RefusedContradiction { get; init; }

    public CropRect ValueCrop { get; init; }

    public IReadOnlyList<string> ValueTokenTexts { get; init; } = [];

    public string? CropText { get; init; }

    public string FrameKind => ImageWidth >= 1600 ? "desktop" : ImageWidth >= 800 ? "framed" : "dialog";
}
