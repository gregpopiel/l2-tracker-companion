using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Plan step 15: reject a tick whose XP / Adena / play time dropped versus
/// the last <em>accepted</em> snapshot (an OCR misread, not a real session).
/// Lamp XP is checked only when both ticks had <c>lampXpRead</c>; a closed
/// Magic Lamp panel is not a misread.
/// </summary>
public static class Monotonicity
{
    public static MonotonicityDecision Evaluate(PlayReport? lastAccepted, PlayReport candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (lastAccepted is null)
        {
            return MonotonicityDecision.Accept();
        }

        if (Dropped("XP", lastAccepted.Xp, candidate.Xp, out var reason)
            || Dropped("Adena", lastAccepted.Adena, candidate.Adena, out reason)
            || Dropped("play time", AsLong(lastAccepted.Minutes), AsLong(candidate.Minutes), out reason))
        {
            return MonotonicityDecision.Reject(reason);
        }

        if (lastAccepted.LampXpRead && candidate.LampXpRead)
        {
            if (Dropped("Red lamp XP", lastAccepted.RedLampXp, candidate.RedLampXp, out reason)
                || Dropped("Purple lamp XP", lastAccepted.PurpleLampXp, candidate.PurpleLampXp, out reason)
                || Dropped("Blue lamp XP", lastAccepted.BlueLampXp, candidate.BlueLampXp, out reason)
                || Dropped("Green lamp XP", lastAccepted.GreenLampXp, candidate.GreenLampXp, out reason)
                || Dropped("lamp XP total", lastAccepted.LampXpTotal, candidate.LampXpTotal, out reason))
            {
                return MonotonicityDecision.Reject(reason);
            }
        }

        return MonotonicityDecision.Accept();
    }

    private static long? AsLong(int? value) => value;

    private static bool Dropped(string field, long? previous, long? candidate, out string reason)
    {
        var inv = CultureInfo.InvariantCulture;
        if (previous is null)
        {
            reason = string.Empty;
            return false;
        }

        if (candidate is null)
        {
            reason = $"{field} could not be read after a previous value of {previous.Value.ToString("N0", inv)}";
            return true;
        }

        if (candidate.Value < previous.Value)
        {
            reason = $"{field} dropped from {previous.Value.ToString("N0", inv)} to {candidate.Value.ToString("N0", inv)}";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}

public sealed record MonotonicityDecision(bool Accepted, string? Reason)
{
    public static MonotonicityDecision Accept() => new(true, null);

    public static MonotonicityDecision Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, reason);
    }
}
