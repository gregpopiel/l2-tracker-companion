namespace L2TrackerCompanion.Ocr;

public sealed class OcrDumpResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public string? OutputPath { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public string? Language { get; init; }

    public int LineCount { get; init; }

    public IReadOnlyList<OcrWord> Words { get; init; } = [];

    public bool FoundPlay { get; init; }

    public bool FoundReport { get; init; }

    public bool FoundCharacters { get; init; }

    public bool FoundAdena { get; init; }

    public IReadOnlyList<string> FoundLampColors { get; init; } = [];

    /// <summary>
    /// Step 5 gate: adena + at least one lamp colour + Play or Report.
    /// Exact "Report" is often misread (Rewrt/Recort); Play + Characters still locate the dialog.
    /// </summary>
    public bool SmokePassed =>
        FoundAdena
        && FoundLampColors.Count > 0
        && (FoundPlay || FoundReport);
}
