using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Compare a tick against the last <em>accepted</em> one. A live Play Report
/// can only grow or stand still, so a drop is either an OCR misread or the
/// player resetting the panel in-game — and those two have to be told apart,
/// because a reset is now the normal way a session begins.
/// </summary>
/// <remarks>
/// Reset signature: the panel's own duration went backwards while neither XP
/// nor Adena grew. Deliberately no ceiling on how far the new duration may
/// have advanced — whether the reset is spotted in its first minute or its
/// fifteenth depends only on when a tick happened to land, and readings stop
/// landing for entirely ordinary reasons (the panel closed, the client
/// relogged, tracking paused). Judging a reset by <em>when we looked</em>
/// rather than <em>what we see</em> wedged the buffer permanently, because a
/// misread is never appended and the stale baseline therefore never ages out.
///
/// A misread hits one field, or moves fields in opposite directions, and
/// self-corrects on the next tick; the cost of mistaking one for a reset is a
/// dropped buffer, not a bad save, because the save is built from a single
/// frame. Lamp XP is compared only
/// when both ticks had <c>lampXpRead</c>; a collapsed Magic Lamp panel is not
/// a misread.
/// </remarks>
public static class Monotonicity
{
    public static MonotonicityDecision Evaluate(PlayReport? lastAccepted, PlayReport candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (lastAccepted is null)
        {
            return MonotonicityDecision.Accept();
        }

        if (LooksLikeReset(lastAccepted, candidate))
        {
            return MonotonicityDecision.Reset();
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

    /// <summary>
    /// The panel was restarted: play time went backwards and nothing else grew.
    /// Every field must be readable — an unread field is a failed OCR pass,
    /// never evidence of a reset.
    /// </summary>
    private static bool LooksLikeReset(PlayReport previous, PlayReport candidate)
    {
        if (previous.Minutes is null || candidate.Minutes is null
            || previous.Xp is null || candidate.Xp is null
            || previous.Adena is null || candidate.Adena is null)
        {
            return false;
        }

        if (candidate.Minutes.Value >= previous.Minutes.Value)
        {
            return false;
        }

        // XP must actually have fallen, not merely failed to grow: a restarted
        // panel starts near zero, so an unchanged XP alongside a shorter
        // duration is the duration line being misread, not a new session.
        // The exception is a baseline that never earned anything, where there
        // is nothing to protect either way.
        var xpFell = candidate.Xp.Value < previous.Xp.Value || previous.Xp.Value == 0;
        return xpFell && candidate.Adena.Value <= previous.Adena.Value;
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

public enum MonotonicityOutcome
{
    /// <summary>Grew or stood still versus the last accepted tick.</summary>
    Accepted,

    /// <summary>Dropped in a way only an in-game reset explains.</summary>
    Reset,

    /// <summary>Dropped in a way only a bad read explains.</summary>
    Misread,
}

public sealed record MonotonicityDecision(MonotonicityOutcome Outcome, string? Reason)
{
    /// <summary>Reset counts as accepted: the figure itself is trustworthy.</summary>
    public bool Accepted => Outcome is MonotonicityOutcome.Accepted or MonotonicityOutcome.Reset;

    public bool IsReset => Outcome is MonotonicityOutcome.Reset;

    public static MonotonicityDecision Accept() => new(MonotonicityOutcome.Accepted, null);

    public static MonotonicityDecision Reset()
        => new(MonotonicityOutcome.Reset, "Play Report was reset in-game — starting a new session.");

    public static MonotonicityDecision Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(MonotonicityOutcome.Misread, reason);
    }
}
