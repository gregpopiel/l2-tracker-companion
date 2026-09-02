using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

public sealed class LampXpResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public string? DumpPath { get; init; }

    public string? TablePngPath { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public CropRect DialogCrop { get; init; }

    public CropRect TableCrop { get; init; }

    public string? AnchorKind { get; init; }

    public long? DialogXp { get; init; }

    public long? DialogAdena { get; init; }

    public bool LampXpRead { get; init; }

    public bool LampPanelClosed { get; init; }

    public bool ExceedsDialogXp { get; init; }

    public long LampXpTotal { get; init; }

    public long? Red { get; init; }

    public long? Purple { get; init; }

    public long? Blue { get; init; }

    public long? Green { get; init; }

    public IReadOnlyList<string> DialogColors { get; init; } = [];

    public IReadOnlyList<string> TableColors { get; init; } = [];

    public string FrameKind => ImageWidth >= 1600 ? "desktop" : ImageWidth >= 800 ? "framed" : "dialog";
}
