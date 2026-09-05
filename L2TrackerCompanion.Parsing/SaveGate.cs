using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// The one place that decides whether a Play Report read may be written to
/// the account, and what colour to show for it.
/// </summary>
/// <remarks>
/// Trust is primarily <em>in-frame</em>: two independent reads of the same
/// field either agree or they do not. Time-based agreement is secondary and
/// only meaningful while the figures are still moving — a player who has
/// stopped farming produces identical frames, and identical frames reproduce
/// an identical misread, so repetition alone can never unlock a save.
///
/// A rejected tick must not lock the player out of Save. <see cref="EvaluateWithHold"/>
/// posts the last frame that itself passed this gate, and only blocks when
/// no such frame exists yet.
/// </remarks>
public static class SaveGate
{
    public static SaveGateDecision Evaluate(
        PlayReport? report,
        DateTimeOffset capturedAt,
        MonotonicityOutcome? lastComparison = null)
    {
        if (report is null)
        {
            // No message: the idle state is already self-evident (a
            // disabled Save button, zeroed totals) — nothing here needs
            // explaining in a status line.
            return SaveGateDecision.Blocked(TrafficLight.Idle, null);
        }

        var warnings = new List<string>();

        if (lastComparison == MonotonicityOutcome.Misread)
        {
            return SaveGateDecision.Blocked(
                TrafficLight.Red,
                "The last read contradicted the one before it.");
        }

        if (report.Confidence.PlayTimeDisagreed)
        {
            return SaveGateDecision.Blocked(
                TrafficLight.Red,
                "The play-time line was read twice and the two reads disagreed.");
        }

        if (report.Confidence.AdenaDisagreed)
        {
            return SaveGateDecision.Blocked(
                TrafficLight.Red,
                Detail("Adena's two reads disagreed", report.Confidence.DescribeAdenaDispute()));
        }

        if (report.Confidence.XpMagnitudeMismatch)
        {
            return SaveGateDecision.Blocked(
                TrafficLight.Red,
                Detail(
                    "The two XP reads disagreed on the number of digits — one of them dropped a digit",
                    report.Confidence.DescribeXpDispute()));
        }

        var snapshot = SessionSnapshot.TryCreate(report, capturedAt);
        if (!snapshot.Ok)
        {
            var light = report.LampPanelClosed ? TrafficLight.Orange : TrafficLight.Red;
            return SaveGateDecision.Blocked(light, snapshot.Error!);
        }

        if (report.Confidence.XpSpliced || report.Confidence.XpDisagreed)
        {
            var inv = CultureInfo.InvariantCulture;
            var saving = report.Xp?.ToString("N0", inv) ?? "(unread)";
            var how = report.Confidence.XpSpliced ? "spliced" : "picked";
            warnings.Add(
                Detail("XP disputed", report.Confidence.DescribeXpDispute())
                + $" Saving {saving} ({how}) — check it against the panel.");
        }

        var colour = warnings.Count > 0 ? TrafficLight.Orange : TrafficLight.Green;
        return new SaveGateDecision(
            CanSave: true,
            Light: colour,
            BlockReason: null,
            Warnings: warnings,
            Totals: snapshot.Totals,
            Source: report,
            UsedHeldRead: false);
    }

    /// <summary>
    /// Prefer the current frame when it was accepted into the session and
    /// itself passes <see cref="Evaluate"/>; otherwise post the last frame
    /// that already passed that gate.
    /// </summary>
    /// <param name="currentAccepted">
    /// False when <paramref name="current"/> was not appended (a monotonicity
    /// drop, a tick that finished after Stop). In-frame agreement is not
    /// enough — that frame must not beat the hold.
    /// </param>
    public static SaveGateDecision EvaluateWithHold(
        PlayReport? current,
        DateTimeOffset currentAt,
        MonotonicityOutcome? lastComparison,
        PlayReport? held,
        DateTimeOffset heldAt,
        bool currentAccepted = true)
    {
        var live = Evaluate(current, currentAt, lastComparison);
        if (currentAccepted && live.CanSave)
        {
            return live;
        }

        if (live.CanSave)
        {
            live = SaveGateDecision.Blocked(
                live.Light == TrafficLight.Green ? TrafficLight.Red : live.Light,
                "The last read was not accepted.");
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

        var warnings = heldDecision.Warnings.ToList();
        var why = live.BlockReason ?? "The current read is not trustworthy.";
        var when = heldAt.ToUniversalTime().UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        warnings.Insert(0, $"{why} Saving last verified read ({when} UTC).");

        var light = live.Light == TrafficLight.Idle ? heldDecision.Light : live.Light;
        return new SaveGateDecision(
            CanSave: true,
            Light: light,
            BlockReason: null,
            Warnings: warnings,
            Totals: heldDecision.Totals,
            Source: held,
            UsedHeldRead: true);
    }

    /// <summary>
    /// Append the two competing figures when both are known, so the message
    /// says what to look at rather than only that something is wrong.
    /// </summary>
    private static string Detail(string headline, string? dispute)
        => dispute is null ? headline + "." : $"{headline} — {dispute}.";
}

public sealed record SaveGateDecision(
    bool CanSave,
    TrafficLight Light,
    string? BlockReason,
    IReadOnlyList<string> Warnings,
    SessionTotals? Totals,
    PlayReport? Source = null,
    bool UsedHeldRead = false)
{
    public static SaveGateDecision Blocked(TrafficLight light, string? reason)
        => new(false, light, reason, [], null);
}
