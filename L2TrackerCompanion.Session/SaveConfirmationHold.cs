namespace L2TrackerCompanion.Session;

/// <summary>
/// Keeps the post-save confirmation (and the in-flight "Saving…" line) from
/// being replaced by the next <c>RefreshSaveEnabled</c> pass — a poll tick,
/// picker change, or game-window refresh would otherwise swap it for the
/// save-lock reason.
/// </summary>
public sealed class SaveConfirmationHold
{
    public bool Active { get; private set; }

    /// <summary>
    /// True while a POST is in flight or a successful save's copy should stay
    /// on screen. Button enablement is a separate concern.
    /// </summary>
    public bool FreezePickerStatus(bool saveInFlight) => saveInFlight || Active;

    /// <summary>
    /// Poll ticks, Capture once, and Parse last must not refill Live status
    /// after a successful save — the companion session is already closed.
    /// </summary>
    public bool IgnoreIncomingReads => Active;

    public void BeginSave() => Active = false;

    public void Saved() => Active = true;

    public void Release() => Active = false;

    public static bool ShouldStopTracking(bool wasTracking, bool saved)
        => wasTracking && saved;
}
