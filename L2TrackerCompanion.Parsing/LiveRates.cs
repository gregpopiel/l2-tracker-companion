using System.Globalization;
using System.Text;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Plan step 23: XP/min and Adena/min from the latest Play Report screenshot
/// (raw OCR totals ÷ play-time minutes). Display-only — not the Save delta.
/// </summary>
public static class LiveRates
{
    public static LiveRatesSnapshot From(PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Minutes is null or <= 0)
        {
            return new LiveRatesSnapshot(null, null, NeedPlayTime: true);
        }

        var minutes = report.Minutes.Value;
        return new LiveRatesSnapshot(
            PerMin(report.Xp, minutes),
            PerMin(report.Adena, minutes),
            NeedPlayTime: false);
    }

    public static string Format(PlayReport? report)
    {
        if (report is null)
        {
            return string.Empty;
        }

        var rates = From(report);
        var inv = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine($"XP/min: {Amt(rates.XpPerMin, rates.NeedPlayTime, inv)}");
        builder.Append($"Adena/min: {Amt(rates.AdenaPerMin, rates.NeedPlayTime, inv)}");
        return builder.ToString();
    }

    private static long? PerMin(long? amount, int minutes)
    {
        if (amount is null)
        {
            return null;
        }

        // decimal keeps every Int64 digit; double is only exact through 2^53.
        return (long)Math.Round(amount.Value / (decimal)minutes, MidpointRounding.AwayFromZero);
    }

    private static string Amt(long? value, bool needPlayTime, CultureInfo inv)
    {
        if (needPlayTime)
        {
            return "(need play time)";
        }

        return value is null ? "(unread)" : value.Value.ToString("N0", inv);
    }
}

public sealed record LiveRatesSnapshot(long? XpPerMin, long? AdenaPerMin, bool NeedPlayTime);
