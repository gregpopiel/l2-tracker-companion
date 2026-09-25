using System.Globalization;
using System.Text;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Plan step 16 / §5: one traffic-light colour for the latest parse.
/// Farm unread or a lamp table that is in frame but unreadable is red;
/// a collapsed Magic Lamp panel is orange, not red; farm+lamps read is green.
/// Missing minimap hint does not change the colour in v1.
/// The live card's XP / Adena / rates come from the save payload (last
/// verified frame), not from a rejected tick – see <see cref="ForDisplay"/>.
/// </summary>
public static class LiveStatus
{
    public static LiveStatusSnapshot Idle()
        => new(TrafficLight.Idle, "No snapshot yet.", null);

    public static LiveStatusSnapshot GameNotRunning()
        => new(TrafficLight.Red, "Game not running.", null);

    public static LiveStatusSnapshot CaptureFailed(string message)
        => new(TrafficLight.Red, message, null);

    public static LiveStatusSnapshot ParseFailed(string message)
        => new(TrafficLight.Red, message, null);

    public static LiveStatusSnapshot FromReport(PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        // Every defect and its wording come from ReadIssues, the same describer
        // SaveGate blocks on – the light and the reason can no longer disagree
        // about a frame, and a clean read is the only thing left to say here.
        var issue = ReadIssues.Describe(report);
        return issue is null
            ? new(TrafficLight.Green, "Farm and lamps read.", report)
            : new(issue.Light, issue.Message, report);
    }

    public static LiveStatusSnapshot TickRejected(string detail)
        => new(TrafficLight.Red, detail, null);

    /// <summary>
    /// The light and sentence the player should see. <paramref name="discarded"/>
    /// is a monotonicity reject the session already dropped. A lamp column
    /// withdrawn because a figure fell is quiet when that withdrawal is the
    /// frame's only defect. A play-time contradiction, an impossible lamp
    /// sum, and an Adena disagreement are quiet the same way. Any of these is painted as
    /// <paramref name="held"/>. A held frame that itself has a defect still
    /// says so. A genuinely unread lamp column, a closed panel, and every
    /// other defect pass through unchanged.
    /// </summary>
    public static LiveStatusSnapshot ForPlayer(
        LiveStatusSnapshot tick,
        PlayReport? candidate,
        PlayReport? held,
        bool discarded)
    {
        var quiet = discarded
            || (candidate is not null
                && (ReadIssues.WithdrawnColumnIsTheOnlyDefect(candidate)
                    || ReadIssues.IsQuietDefect(candidate)));
        if (held is null || !quiet)
        {
            return tick;
        }

        return FromReport(held);
    }

    /// <summary>
    /// What to paint when this tick produced no report. The light follows the
    /// sentence the banner will keep. A hold reason or a block reason stays
    /// red or orange. A quiet hold is the held frame. An empty session stays
    /// idle.
    /// </summary>
    public static LiveStatusSnapshot ForInterruptedTick(SaveGateDecision gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        if (gate.HoldReason is not null)
        {
            return new LiveStatusSnapshot(gate.Light, gate.HoldReason, gate.Source);
        }

        if (gate.Source is not null)
        {
            return FromReport(gate.Source);
        }

        if (gate.BlockReason is not null)
        {
            return new LiveStatusSnapshot(gate.Light, gate.BlockReason, null);
        }

        return Idle();
    }

    /// <summary>
    /// Keep this tick's light and message, but show the numbers Save would
    /// post – the last verified frame, not a rejected OCR.
    /// </summary>
    public static LiveStatusSnapshot ForDisplay(LiveStatusSnapshot tick, PlayReport? saveSource)
        => tick with { Report = saveSource };

    public static string Format(LiveStatusSnapshot status)
    {
        var builder = new StringBuilder();
        builder.Append("Light: ");
        builder.AppendLine(status.Light.ToString());
        builder.AppendLine(status.Detail);
        var rates = LiveRates.Format(status.Report);
        if (rates.Length > 0)
        {
            builder.AppendLine(rates);
        }

        var values = FormatValues(status.Report);
        if (values.Length > 0)
        {
            builder.Append(values);
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatValues(PlayReport? report)
    {
        if (report is null)
        {
            return string.Empty;
        }

        var inv = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine($"XP: {Amt(report.Xp, inv)}");
        builder.AppendLine($"Adena: {Amt(report.Adena, inv)}");
        builder.AppendLine($"Play time: {(report.Minutes is null ? "(unread)" : report.Minutes.Value.ToString(inv) + " min")}");
        builder.Append("Lamps: ");
        if (report.LampPanelClosed)
        {
            builder.AppendLine("closed");
        }
        else if (report.LampXpExceedsDialog)
        {
            builder.AppendLine("discarded");
        }
        else if (report.LampXpRead)
        {
            builder.AppendLine(
                $"R={Amt(report.RedLampXp, inv)}  P={Amt(report.PurpleLampXp, inv)}  "
                + $"B={Amt(report.BlueLampXp, inv)}  G={Amt(report.GreenLampXp, inv)}");
        }
        else
        {
            builder.AppendLine("unread");
        }

        builder.Append($"Location: {report.LocationHint ?? "(not visible)"}");
        return builder.ToString();
    }

    private static string Amt(long? value, CultureInfo inv)
        => value is null ? "(unread)" : value.Value.ToString("N0", inv);
}

public enum TrafficLight
{
    Idle,
    Red,
    Orange,
    Green,
}

public sealed record LiveStatusSnapshot(TrafficLight Light, string Detail, PlayReport? Report);
