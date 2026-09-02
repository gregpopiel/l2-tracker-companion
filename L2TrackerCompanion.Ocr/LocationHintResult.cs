namespace L2TrackerCompanion.Ocr;

public sealed class LocationHintResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public string? DumpPath { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public string? Hint { get; init; }

    public int ZoneWordCount { get; init; }

    public string FrameKind => ImageWidth >= 1600 ? "desktop" : ImageWidth >= 800 ? "framed" : "dialog";
}
