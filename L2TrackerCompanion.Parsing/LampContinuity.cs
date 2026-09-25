using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Lamp XP only ever grows within a session. A figure that falls is a bad
/// read – including the <c>0</c> a located-but-unparsed cell is given so a
/// genuine empty lamp can still be saved – and the column is withdrawn
/// rather than allowed to discard the rest of the frame.
/// </summary>
/// <remarks>
/// Compared against the last frame that actually read lamps, not merely the
/// last accepted frame. The frame just withdrawn has no lamp figures, and
/// the next failing cell would otherwise find nothing to contradict its zero.
/// <para>
/// The first lamp-reading frame of a session has nothing to contradict a
/// synthesized zero, so it is kept until a real figure arrives a tick later.
/// Unavoidable without also rejecting a genuine all-zero session start, and
/// it heals itself on the next real read.
/// </para>
/// </remarks>
public static class LampContinuity
{
    /// <summary>
    /// Tail of the warning <see cref="Withdraw"/> appends. The player-facing
    /// status uses it to tell a dropped column from a column that never parsed.
    /// </summary>
    public const string WithdrawnMark = "the column was treated as unread";

    public static bool WasWithdrawn(PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var warning in report.Warnings)
        {
            if (warning.Contains(WithdrawnMark, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static PlayReport Withdraw(PlayReport? previous, PlayReport? lastLampRead, PlayReport candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // Already unread, including a second pass over a frame this method
        // just withdrew. Idempotent so the UI and TryAccept can both call it.
        if (!candidate.LampXpRead)
        {
            return candidate;
        }

        if (previous is not null && Monotonicity.LooksLikeReset(previous, candidate))
        {
            return candidate;
        }

        if (lastLampRead is null || !lastLampRead.LampXpRead)
        {
            return candidate;
        }

        var drop = FirstDrop(lastLampRead, candidate);
        if (drop is null)
        {
            return candidate;
        }

        var (name, before, after) = drop.Value;
        var inv = CultureInfo.InvariantCulture;
        var warning =
            $"{name} read {after.ToString("N0", inv)} after {before.ToString("N0", inv)} "
            + "– lamp XP only ever grows, so " + WithdrawnMark;
        return candidate with
        {
            LampXpRead = false,
            RedLampXp = null,
            PurpleLampXp = null,
            BlueLampXp = null,
            GreenLampXp = null,
            LampXpTotal = 0,
            Warnings = candidate.Warnings.Append(warning).ToArray(),
        };
    }

    /// <summary>
    /// The first colour that fell, in red / purple / blue / green / total
    /// order, or null when every lamp figure held or grew.
    /// </summary>
    private static (string Name, long Before, long After)? FirstDrop(PlayReport baseline, PlayReport candidate)
    {
        (string Name, long? Before, long? After)[] fields =
        [
            ("Red lamp XP", baseline.RedLampXp, candidate.RedLampXp),
            ("Purple lamp XP", baseline.PurpleLampXp, candidate.PurpleLampXp),
            ("Blue lamp XP", baseline.BlueLampXp, candidate.BlueLampXp),
            ("Green lamp XP", baseline.GreenLampXp, candidate.GreenLampXp),
            ("lamp XP total", baseline.LampXpTotal, candidate.LampXpTotal),
        ];

        foreach (var field in fields)
        {
            if (field.Before is long before && field.After is long after && after < before)
            {
                return (field.Name, before, after);
            }
        }

        return null;
    }
}
