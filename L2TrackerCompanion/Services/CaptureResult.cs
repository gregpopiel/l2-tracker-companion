namespace L2TrackerCompanion.Services;

public sealed class CaptureResult
{
    public required bool Success { get; init; }

    public string? OutputPath { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>
    /// True when the bitmap saved but appears blank or nearly black — PrintWindow may not work for this client.
    /// </summary>
    public bool IsLikelyBlank { get; init; }
}
