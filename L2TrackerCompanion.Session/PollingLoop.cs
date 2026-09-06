using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Session;

/// <summary>
/// Plan step 15: Start/Stop gate for the capture→OCR→accept tick, plus the
/// cadence that tick should run at. The WPF timer still owns the actual
/// scheduling; this type only says whether tracking is on — so a tick that
/// finishes after Stop does not append — and how long the gap before the next
/// read should be.
/// </summary>
public sealed class PollingLoop
{
    /// <summary>Steady cadence, once the warm-up is over.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gap between the warm-up reads. Short enough to fill the location window
    /// in seconds rather than most of a minute, long enough that the tick's own
    /// cost (a synchronous window capture plus OCR) still fits inside it.
    /// </summary>
    public static readonly TimeSpan WarmUpInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reads that run at <see cref="WarmUpInterval"/> before the cadence drops
    /// to <see cref="Interval"/>. Tied to the location window on purpose:
    /// filling that window is the only thing the warm-up exists for, so the two
    /// must not drift apart. At the steady cadence alone the spot cannot resolve
    /// itself for the first 40 seconds of a session.
    /// </summary>
    public const int WarmUpReads = LocationStability.WindowSize;

    /// <summary>
    /// Ceiling on warm-up attempts, whatever they produced. Progress is counted
    /// in reads that actually reached the window, so on its own it would never
    /// advance while the game is closed or the minimap is unreadable — this is
    /// what stops the faster cadence running indefinitely in that case.
    /// Generous rather than tight: an attempt that produced nothing costs only
    /// the tick itself, whereas ending the warm-up early costs the whole
    /// feature.
    /// </summary>
    public const int MaxWarmUpAttempts = 3 * WarmUpReads;

    private int _reads;
    private int _attempts;

    public bool IsRunning { get; private set; }

    public bool IsWarmingUp => IsRunning && _reads < WarmUpReads && _attempts < MaxWarmUpAttempts;

    /// <summary>Gap to wait before arming the next read.</summary>
    public TimeSpan NextInterval => IsWarmingUp ? WarmUpInterval : Interval;

    /// <summary>
    /// One poll attempt was made, whatever came of it. Only bounds the warm-up;
    /// it does not advance it.
    /// </summary>
    public void NoteAttempt()
    {
        if (_attempts < MaxWarmUpAttempts)
        {
            _attempts++;
        }
    }

    /// <summary>
    /// A read reached the location window — appended, carrying a hint. This is
    /// what the warm-up is counting: an attempt that captured nothing, failed to
    /// parse, was discarded by monotonicity, or read no minimap name contributed
    /// nothing to the window, so it must not spend the budget meant to fill it.
    /// </summary>
    public void NoteRead()
    {
        if (_reads < WarmUpReads)
        {
            _reads++;
        }
    }

    /// <summary>
    /// The session buffer was dropped, taking the gathered hints with it (an
    /// in-game reset, a stale baseline, a restarted client). The window is
    /// starting over, so the reads counted towards it are gone too — but the
    /// attempt ceiling deliberately is not, since it bounds the whole run.
    /// </summary>
    public void RestartWarmUpProgress() => _reads = 0;

    /// <summary>A new run warms up again — its location window starts empty.</summary>
    public void Start()
    {
        IsRunning = true;
        _reads = 0;
        _attempts = 0;
    }

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
            ? TickResult.AfterReset(accepted.Row!, accepted.Reason!)
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

    public static TickResult AfterReset(SnapshotRow row, string reason)
        => new(true, true, reason, row, MonotonicityOutcome.Reset);

    public static TickResult Discarded(string reason)
        => new(true, false, $"Discarded: {reason}.", null, MonotonicityOutcome.Misread);
}
