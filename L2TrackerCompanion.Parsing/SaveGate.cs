using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// The one place that decides whether the current reading may be written to
/// the account, and what colour to show for it.
/// </summary>
/// <remarks>
/// Trust is primarily <em>in-frame</em>: two independent reads of the same
/// field either agree or they do not. Time-based agreement is secondary and
/// only meaningful while the figures are still moving — a player who has
/// stopped farming produces identical frames, and identical frames reproduce
/// an identical misread, so repetition alone can never unlock a save.
/// </remarks>
public static class SaveGate
{
    public static SaveGateDecision Evaluate(
        PlayReport? report,
        DateTimeOffset capturedAt,
        MonotonicityOutcome? lastComparison = null,
        bool saveLocked = false)
    {
        if (report is null)
        {
            return SaveGateDecision.Blocked(TrafficLight.Idle, "No reading yet.");
        }

        var warnings = new List<string>();

        if (lastComparison == MonotonicityOutcome.Misread)
        {
            return SaveGateDecision.Blocked(
                TrafficLight.Red,
                "The last reading contradicted the one before it — waiting for a clean read.");
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
                Detail("Adena's two reads disagreed", report.Confidence.DescribeAdenaDispute())
                    + " Save blocked.");
        }

        if (report.Confidence.XpMagnitudeMismatch)
        {
            return SaveGateDecision.Blocked(
                TrafficLight.Red,
                Detail(
                    "The two XP reads disagreed on the number of digits — one of them dropped a digit",
                    report.Confidence.DescribeXpDispute())
                    + " Save blocked.");
        }

        var snapshot = SessionSnapshot.TryCreate(report, capturedAt);
        if (!snapshot.Ok)
        {
            var light = report.LampPanelClosed ? TrafficLight.Orange : TrafficLight.Red;
            return SaveGateDecision.Blocked(light, snapshot.Error!);
        }

        // Cumulative panel: a later frame still covers every minute already
        // posted, so one save per reset — not one per distinct frame.
        if (saveLocked)
        {
            return SaveGateDecision.Blocked(
                TrafficLight.Orange,
                "This Play Report has already been saved. Reset it in-game to start a new session.");
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
            Totals: snapshot.Totals);
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
    SessionTotals? Totals)
{
    public static SaveGateDecision Blocked(TrafficLight light, string reason)
        => new(false, light, reason, [], null);
}
