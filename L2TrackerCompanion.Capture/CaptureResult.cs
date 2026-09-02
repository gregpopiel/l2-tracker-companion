namespace L2TrackerCompanion.Capture;

public sealed class CaptureResult
{
    public required bool Success { get; init; }

    public string OutputPath { get; init; }

    public string ErrorMessage { get; init; }

    public bool IsLikelyBlank { get; init; }
}
