using System.Globalization;
using System.Text;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Which time unit the live XP / Adena rates are shown in. Hour is the
/// website schema default; Minute is <c>user_settings.rate_unit = minute</c>.
/// </summary>
public enum RateUnit
{
    Minute,
    Hour,
}

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
            PerUnit(report.Xp, minutes, scale: 1),
            PerUnit(report.Adena, minutes, scale: 1),
            NeedPlayTime: false);
    }

    public static string Format(PlayReport? report, RateUnit unit = RateUnit.Minute)
    {
        if (report is null)
        {
            return string.Empty;
        }

        var rates = From(report);
        var suffix = unit == RateUnit.Hour ? "h" : "min";
        var scale = unit == RateUnit.Hour ? 60 : 1;
        var inv = CultureInfo.InvariantCulture;
        long? xp = null;
        long? adena = null;
        if (!rates.NeedPlayTime)
        {
            var minutes = report.Minutes!.Value;
            xp = PerUnit(report.Xp, minutes, scale);
            adena = PerUnit(report.Adena, minutes, scale);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"XP/{suffix}: {Amt(xp, rates.NeedPlayTime, inv)}");
        builder.Append($"Adena/{suffix}: {Amt(adena, rates.NeedPlayTime, inv)}");
        return builder.ToString();
    }

    private static long? PerUnit(long? amount, int minutes, int scale)
    {
        if (amount is null)
        {
            return null;
        }

        // decimal keeps every Int64 digit; double is only exact through 2^53.
        return (long)Math.Round(amount.Value * (decimal)scale / minutes, MidpointRounding.AwayFromZero);
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
