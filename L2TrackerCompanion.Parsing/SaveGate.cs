namespace L2TrackerCompanion.Parsing;

/// <summary>
/// The one place that decides whether a Play Report read may be written to
/// the account, and what colour to show for it.
/// </summary>
/// <remarks>
/// Trust is primarily <em>in-frame</em>: two independent reads of the same
/// field either agree or they do not. Time-based agreement is secondary and
/// only meaningful while the figures are still moving – a player who has
/// stopped farming produces identical frames, and identical frames reproduce
/// an identical misread, so repetition alone can never unlock a save.
///
/// What is wrong with a frame is not decided here: <see cref="ReadIssues"/>
/// answers that for this class and for <see cref="LiveStatus"/> alike, so the
/// gate and the traffic light can never phrase the same defect two ways.
///
/// A rejected tick must not lock the player out of Save. <see cref="EvaluateWithHold"/>
/// posts the last frame that itself passed this gate, and only blocks when
/// no such frame exists yet.
/// </remarks>
public static class SaveGate
{
    public static SaveGateDecision Evaluate(PlayReport? report, DateTimeOffset capturedAt)
    {
        if (report is null)
        {
            // No message: the idle state is already self-evident (a
            // disabled Save button, zeroed totals) – nothing here needs
            // explaining in a status line.
            return SaveGateDecision.Blocked(TrafficLight.Idle, null);
        }

        var issue = ReadIssues.Describe(report);
        if (issue is { BlocksSave: true })
        {
            return SaveGateDecision.Blocked(issue.Light, issue.Message, issue);
        }

        var snapshot = SessionSnapshot.TryCreate(report, capturedAt);
        if (!snapshot.Ok)
        {
            // Defensive only: ReadIssues covers everything TryCreate rejects,
            // so reaching this means the two fell out of step – surface the
            // snapshot's own reason rather than an empty status line.
            var light = report.LampPanelClosed ? TrafficLight.Orange : TrafficLight.Red;
            return SaveGateDecision.Blocked(light, snapshot.Error!);
        }

        var colour = issue?.Light ?? TrafficLight.Green;
        return new SaveGateDecision(
            CanSave: true,
            Light: colour,
            BlockReason: null,
            Totals: snapshot.Totals,
            Source: report,
            UsedHeldRead: false,
            Issue: issue);
    }

    /// <summary>
    /// Prefer the current frame when it was accepted into the session and
    /// itself passes <see cref="Evaluate"/>; otherwise post the last frame
    /// that already passed that gate.
    /// </summary>
    /// <param name="currentAccepted">
    /// False when <paramref name="current"/> was not appended (a monotonicity
    /// drop, a tick that finished after Stop). In-frame agreement is not
    /// enough – that frame must not beat the hold.
    /// </param>
    public static SaveGateDecision EvaluateWithHold(
        PlayReport? current,
        DateTimeOffset currentAt,
        PlayReport? held,
        DateTimeOffset heldAt,
        bool currentAccepted = true)
    {
        var live = Evaluate(current, currentAt);
        if (currentAccepted && live.CanSave)
        {
            return live;
        }

        if (live.CanSave)
        {
            live = SaveGateDecision.Blocked(
                live.Light == TrafficLight.Green ? TrafficLight.Red : live.Light,
                "The last read was not accepted.",
                live.Issue);
        }

        if (held is null)
        {
            return live;
        }

        var heldDecision = Evaluate(held, heldAt);
        if (!heldDecision.CanSave || heldDecision.Totals is null)
        {
            return live;
        }

        // The live card is already painted from this held frame, so a sentence
        // saying which read is being saved restated a fact nobody acts on.
        // HoldReason is why the current frame was passed over – only when
        // Evaluate actually named a defect. A missing current frame (game not
        // running, empty store) has BlockReason null on purpose; inventing
        // "not trustworthy" there duplicated the live-status line
        // ("Game not running. · The current read is not trustworthy.").
        var light = live.Light == TrafficLight.Idle ? heldDecision.Light : live.Light;
        return new SaveGateDecision(
            CanSave: true,
            Light: light,
            BlockReason: null,
            Totals: heldDecision.Totals,
            Source: held,
            UsedHeldRead: true,
            Issue: heldDecision.Issue,
            HoldReason: live.BlockReason);
    }
}

public sealed record SaveGateDecision(
    bool CanSave,
    TrafficLight Light,
    string? BlockReason,
    SessionTotals? Totals,
    PlayReport? Source = null,
    bool UsedHeldRead = false,
    ReadIssue? Issue = null,
    string? HoldReason = null)
{
    public static SaveGateDecision Blocked(TrafficLight light, string? reason, ReadIssue? issue = null)
        => new(false, light, reason, null, Issue: issue);
}
