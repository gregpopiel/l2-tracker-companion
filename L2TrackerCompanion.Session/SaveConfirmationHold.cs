namespace L2TrackerCompanion.Session;

/// <summary>
/// After a 2xx, ignore incoming Play Report reads until the player starts
/// a new run. Confirmation and errors live on the save-result banner, and
/// in-flight copy lives on the Save button – this hold does not freeze
/// either of those surfaces.
/// </summary>
public sealed class SaveConfirmationHold
{
    public bool Active { get; private set; }

    /// <summary>
    /// Poll ticks, Capture once, and Parse last must not refill Live status
    /// after a successful save – the companion session is already closed.
    /// </summary>
    public bool IgnoreIncomingReads => Active;

    public void BeginSave() => Active = false;

    public void Saved() => Active = true;

    public void Release() => Active = false;

    public static bool ShouldStopTracking(bool wasTracking, bool saved)
        => wasTracking && saved;
}
