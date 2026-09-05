using System.Text;

namespace L2TrackerCompanion.Api;

/// <summary>
/// Where the pace being read right now would place among the spots this
/// character has already farmed. Answers "is this any good?", which the raw
/// XP/h figure on its own cannot.
/// </summary>
/// <remarks>
/// Deliberately a rank and nothing else. The Play Report counts from login and
/// only resets when the game restarts, so a read taken after moving spots
/// blends both of them — which leaves a rank against the whole list honest,
/// and would make any "vs this spot's average" figure a lie until the player
/// restarts the report. Averages come straight from <c>GET /api/spots</c>
/// rather than being recomputed from logs, so the ordering here matches the
/// XP/H columns the website's Spots tab shows for the same character.
/// </remarks>
public static class SpotBenchmark
{
    public static SpotBenchmarkSnapshot Evaluate(
        long? liveXpPerHour,
        long? liveAdenaPerHour,
        IEnumerable<SpotInfo>? spots,
        int? areaId = null,
        long? livePureXpPerHour = null)
    {
        // A spot the endpoint returned without averages is one this character
        // has never logged. It is not a zero-rate spot, so it is not last —
        // it is absent, and must not pad the denominator either.
        var pool = (spots ?? []).Where(spot => spot.AverageXpHourly is not null);

        // By id, not by Area.Name: PostSpotAsync builds a SpotInfo with a null
        // Area (the create response carries no area), so a freshly auto-created
        // spot would drop out of its own area until the list is refetched.
        if (areaId is int id)
        {
            pool = pool.Where(spot => spot.AreaId == id);
        }

        var ranked = pool.ToList();
        return new SpotBenchmarkSnapshot(
            RankedSpots: ranked.Count,
            XpRank: Rank(liveXpPerHour, ranked.Select(spot => spot.AverageXpHourly)),
            PureXpRank: Rank(livePureXpPerHour, ranked.Select(spot => spot.FarmXpHourly)),
            AdenaRank: Rank(liveAdenaPerHour, ranked.Select(spot => spot.AdenaHourly)),
            HasLiveRate: liveXpPerHour is not null || liveAdenaPerHour is not null || livePureXpPerHour is not null,
            AreaFiltered: areaId is not null);
    }

    public static string Format(SpotBenchmarkSnapshot snapshot, bool perHour)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Nothing was read this frame — the rates above already say so.
        if (!snapshot.HasLiveRate)
        {
            return string.Empty;
        }

        if (snapshot.RankedSpots == 0)
        {
            return snapshot.AreaFiltered
                ? "No sessions in this area yet."
                : "No saved sessions yet — nothing to compare against.";
        }

        // The rank itself is unit-free; the label follows the rates above it so
        // the card does not read as if it mixed two different units.
        var suffix = perHour ? "h" : "min";
        var builder = new StringBuilder();
        Append(builder, $"XP/{suffix}", snapshot.XpRank, snapshot.RankedSpots);
        Append(builder, $"Net XP/{suffix}", snapshot.PureXpRank, snapshot.RankedSpots);
        Append(builder, $"Adena/{suffix}", snapshot.AdenaRank, snapshot.RankedSpots);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string label, int? rank, int total)
    {
        if (rank is null)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append($"{label}: #{rank} of {total} spot{(total == 1 ? string.Empty : "s")}");
    }

    /// <summary>
    /// One-based position of <paramref name="live"/> among the stored averages,
    /// ties resolved in the read's favour. Null when there is nothing to
    /// place, or nothing to place it against.
    /// </summary>
    private static int? Rank(long? live, IEnumerable<long?> averages)
    {
        if (live is null)
        {
            return null;
        }

        var scaled = averages
            .Where(average => average is not null)
            // The one place the ×1000 storage convention is undone. Without it
            // every read outranks every spot and the feature still looks
            // like it works — see LegacyThousands.
            .Select(average => LegacyThousands.ToRaw(average)!.Value)
            .ToList();

        return scaled.Count == 0 ? null : 1 + scaled.Count(average => average > live.Value);
    }
}

public sealed record SpotBenchmarkSnapshot(
    int RankedSpots,
    int? XpRank,
    int? AdenaRank,
    bool HasLiveRate,
    bool AreaFiltered,
    int? PureXpRank = null);

/// <summary>
/// One entry of the "compare against" picker: every area the account has, plus
/// <see cref="All"/>. Areas with no history for the current character are kept
/// deliberately — the list then stays the same whichever character is chosen,
/// and a chosen area survives switching between them.
/// </summary>
public sealed record AreaChoice(int? AreaId, string Label)
{
    public const string AllLabel = "All";

    public static AreaChoice All { get; } = new(null, AllLabel);

    public static IReadOnlyList<AreaChoice> Build(IEnumerable<AreaInfo>? areas)
        => [All, .. (areas ?? [])
            .Where(area => !string.IsNullOrWhiteSpace(area.Name))
            .Select(area => new AreaChoice(area.Id, area.Name.Trim()))];
}
