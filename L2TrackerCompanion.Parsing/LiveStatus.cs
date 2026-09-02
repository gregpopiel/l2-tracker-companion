using System.Globalization;
using System.Text;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Plan step 16 / §5: one traffic-light colour for the latest parse.
/// Farm unread or a lamp table that is in frame but unreadable is red;
/// a collapsed Magic Lamp panel is orange, not red; farm+lamps read is green.
/// Missing minimap hint does not change the colour in v1.
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

        if (report.UnreadFields.Count > 0)
        {
            return new(TrafficLight.Red, DescribeFarmUnread(report.UnreadFields), report);
        }

        if (report.LampPanelClosed)
        {
            return new(TrafficLight.Orange, "Magic Lamp panel closed.", report);
        }

        if (!report.LampXpRead)
        {
            var detail = report.LampXpExceedsDialog
                ? "Lamp XP discarded (sum exceeds dialog XP)."
                : "Lamp table XP column couldn't be read.";
            return new(TrafficLight.Red, detail, report);
        }

        return new(TrafficLight.Green, "Farm and lamps read.", report);
    }

    public static string Format(LiveStatusSnapshot status)
    {
        var builder = new StringBuilder();
        builder.Append("Light: ");
        builder.AppendLine(status.Light.ToString());
        builder.AppendLine(status.Detail);
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

    private static string DescribeFarmUnread(IReadOnlyList<string> unread)
    {
        if (unread.Count >= 3)
        {
            return "Couldn't read farm data.";
        }

        if (unread.Count == 1)
        {
            return $"Couldn't read {unread[0]}.";
        }

        return $"Couldn't read {unread[0]} and {unread[1]}.";
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
