using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Session;

/// <summary>
/// Plan step 15: Start/Stop gate for the 10s capture→OCR→accept tick.
/// The WPF timer owns the interval; this type only knows whether tracking
/// is on, so a tick that finishes after Stop does not append.
/// </summary>
public sealed class PollingLoop
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public TickResult Tick(SessionStore store, PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(report);
        if (!IsRunning)
        {
            return TickResult.NotTracking();
        }

        var accepted = store.TryAccept(report);
        if (!accepted.Appended)
        {
            return TickResult.Discarded(accepted.Reason!);
        }

        return accepted.WasReset
            ? TickResult.AfterReset(accepted.Row!)
            : TickResult.Accepted(accepted.Row!);
    }
}

public sealed record TickResult(
    bool Tracking,
    bool Appended,
    string Message,
    SnapshotRow? Row,
    MonotonicityOutcome? Outcome = null)
{
    public static TickResult NotTracking()
        => new(false, false, "Not tracking.", null);

    public static TickResult Accepted(SnapshotRow row)
        => new(true, true, $"Accepted #{row.Id}.", row, MonotonicityOutcome.Accepted);

    public static TickResult AfterReset(SnapshotRow row)
        => new(
            true,
            true,
            "Play Report was reset in-game — new session started.",
            row,
            MonotonicityOutcome.Reset);

    public static TickResult Discarded(string reason)
        => new(true, false, $"Discarded: {reason}.", null, MonotonicityOutcome.Misread);
}
