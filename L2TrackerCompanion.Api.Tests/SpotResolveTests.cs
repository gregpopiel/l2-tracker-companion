using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class SpotResolveTests
{
    private static readonly AreaInfo World = new(1, "World");

    private static readonly SpotInfo Dragon = new(
        10, "Dragon Valley (east)", 1, new SpotAreaInfo(1, "World"));

    private static readonly SpotInfo Aligator = new(
        184, "Aligator", 2, new SpotAreaInfo(2, "Special Zone"));

    private static readonly IReadOnlyList<SpotInfo> Spots = [Dragon, Aligator];

    private static SpotResolveDecision Resolve(
        SpotInfo? selected,
        string? stableHint,
        IEnumerable<SpotInfo>? spots = null,
        AreaInfo? worldArea = null,
        bool worldSpecified = false,
        string? currentHint = null,
        bool spotsLoaded = true,
        bool useStableAsCurrent = true)
    {
        if (spotsLoaded && spots is null)
        {
            spots = Spots;
        }

        var world = worldSpecified ? worldArea : (worldArea ?? World);
        var current = useStableAsCurrent ? (currentHint ?? stableHint) : currentHint;
        return SpotResolve.Evaluate(selected, stableHint, current, spots, spotsLoaded, world);
    }

    [Fact]
    public void ASelectedSpotWinsOverAStableHint()
    {
        var decision = Resolve(Aligator, "Dragon Valley (east)");

        Assert.Equal(SpotResolveKind.UseSelected, decision.Kind);
        Assert.Same(Aligator, decision.Spot);
        Assert.True(decision.CanSave);
    }

    [Fact]
    public void AStableHintUsesTheUniqueExistingSpot()
    {
        var decision = Resolve(null, "Dragon Valley (east)");

        Assert.Equal(SpotResolveKind.UseExisting, decision.Kind);
        Assert.Same(Dragon, decision.Spot);
        Assert.Equal("Save will use existing spot: Dragon Valley (east).", decision.Hint(0, 0, 5));
    }

    [Fact]
    public void AStableUnknownNameCreatesAWorldSpot()
    {
        var decision = Resolve(null, "Brand New Camp");

        Assert.Equal(SpotResolveKind.CreateWorld, decision.Kind);
        Assert.Equal("Brand New Camp", decision.Name);
        Assert.Same(World, decision.WorldArea);
        Assert.Equal("Save will create a new World spot: Brand New Camp.", decision.Hint(0, 0, 5));
    }

    [Fact]
    public void AnEmptyLoadedListCanStillCreate()
    {
        var decision = Resolve(null, "Brand New Camp", spots: []);

        Assert.Equal(SpotResolveKind.CreateWorld, decision.Kind);
        Assert.True(decision.CanSave);
    }

    [Fact]
    public void UnloadedSpotsDoNotCreateEvenWhenTheHintIsStable()
    {
        var decision = Resolve(
            null,
            "Brand New Camp",
            spots: null,
            spotsLoaded: false);

        Assert.Equal(SpotResolveKind.SpotsNotLoaded, decision.Kind);
        Assert.False(decision.CanSave);
        Assert.Equal("Spots have not loaded yet.", decision.Hint(5, 4, 5));
    }

    [Fact]
    public void NoStableHintNeedsThePicker()
    {
        var decision = Resolve(null, null);

        Assert.Equal(SpotResolveKind.Unstable, decision.Kind);
        Assert.False(decision.CanSave);
        Assert.Equal(
            "Pick a spot, or keep tracking until Location is stable (2/5).",
            decision.Hint(2, 0, 5));
    }

    [Fact]
    public void CurrentHintMustMatchTheStableName()
    {
        var decision = Resolve(
            null,
            "Dragon Valley (east)",
            currentHint: "Somewhere Else",
            useStableAsCurrent: false);

        Assert.Equal(SpotResolveKind.CurrentMismatch, decision.Kind);
        Assert.False(decision.CanSave);
        Assert.Contains("Dragon Valley (east)", decision.Hint(5, 4, 5), StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankCurrentHintDoesNotUseTheMajorityName()
    {
        var decision = Resolve(
            null,
            "Dragon Valley (east)",
            currentHint: null,
            useStableAsCurrent: false);

        Assert.Equal(SpotResolveKind.CurrentMismatch, decision.Kind);
        Assert.False(decision.CanSave);
    }

    [Fact]
    public void TwoCaseVariantsAreAmbiguous()
    {
        var spots = new[]
        {
            Dragon,
            new SpotInfo(11, "dragon valley (east)", 1, new SpotAreaInfo(1, "World")),
        };

        var decision = Resolve(null, "Dragon Valley (east)", spots);

        Assert.Equal(SpotResolveKind.Ambiguous, decision.Kind);
        Assert.False(decision.CanSave);
        Assert.Contains("Multiple spots match", decision.Hint(5, 4, 5), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateIsBlockedWhenWorldIsMissing()
    {
        var decision = Resolve(null, "Brand New Camp", worldArea: null, worldSpecified: true);

        Assert.Equal(SpotResolveKind.MissingWorld, decision.Kind);
        Assert.False(decision.CanSave);
    }

    [Fact]
    public void MatchingIgnoresHintCase()
    {
        var decision = Resolve(null, "  DRAGON VALLEY (EAST)  ");

        Assert.Equal(SpotResolveKind.UseExisting, decision.Kind);
        Assert.Same(Dragon, decision.Spot);
    }

    /// <summary>
    /// Minimap OCR of a zone label rarely repeats a spelling, so the 4-of-5
    /// window can go a whole session without settling. One read that exactly
    /// names an owned spot is the stronger signal and does not wait for it.
    /// </summary>
    [Fact]
    public void ASingleReadThatNamesAnOwnedSpotNeedsNoWindow()
    {
        var decision = Resolve(
            null,
            stableHint: null,
            currentHint: "Dragon Valley (east)",
            useStableAsCurrent: false);

        Assert.Equal(SpotResolveKind.UseExisting, decision.Kind);
        Assert.Same(Dragon, decision.Spot);
        Assert.True(decision.CanSave);
    }

    /// <summary>
    /// The safety boundary of the shortcut: matching a closed vocabulary is
    /// safe, inventing a name from one OCR read is not — it would put a
    /// misreading like "Selfrlahum Base" into the account permanently.
    /// </summary>
    [Fact]
    public void ASingleReadNeverCreatesASpot()
    {
        var decision = Resolve(
            null,
            stableHint: null,
            currentHint: "Brand New Camp",
            useStableAsCurrent: false);

        Assert.Equal(SpotResolveKind.Unstable, decision.Kind);
        Assert.False(decision.CanSave);
    }

    [Fact]
    public void ASingleReadOverridesAStableHintForADifferentSpot()
    {
        var decision = Resolve(
            null,
            stableHint: "Dragon Valley (east)",
            currentHint: "Aligator",
            useStableAsCurrent: false);

        Assert.Equal(SpotResolveKind.UseExisting, decision.Kind);
        Assert.Same(Aligator, decision.Spot);
    }

    [Fact]
    public void APickedSpotStillBeatsASingleReadMatch()
    {
        var decision = Resolve(
            Dragon,
            stableHint: null,
            currentHint: "Aligator",
            useStableAsCurrent: false);

        Assert.Equal(SpotResolveKind.UseSelected, decision.Kind);
        Assert.Same(Dragon, decision.Spot);
    }

    [Fact]
    public void ASingleReadMatchingTwoSpotsFallsThroughToAmbiguous()
    {
        var spots = new[]
        {
            Dragon,
            new SpotInfo(11, "dragon valley (east)", 1, new SpotAreaInfo(1, "World")),
        };

        var decision = Resolve(null, "Dragon Valley (east)", spots);

        Assert.Equal(SpotResolveKind.Ambiguous, decision.Kind);
    }

    [Fact]
    public void DetectedNamePrefersAnOwnedSpotOverTheWindow()
    {
        Assert.Equal(
            "Aligator",
            SpotResolve.DetectedName("Aligator", "Dragon Valley (east)", Spots));
    }

    [Fact]
    public void DetectedNameFallsBackToTheWindow()
    {
        Assert.Equal(
            "Dragon Valley (east)",
            SpotResolve.DetectedName("Selfrlahum Base", "  Dragon Valley (east)  ", Spots));
    }

    [Fact]
    public void DetectedNameIsNullWhenNeitherHolds()
    {
        // A garbled hint with nothing settled behind it is evidence of nothing,
        // so the mismatch warning must stay silent rather than guess.
        Assert.Null(SpotResolve.DetectedName("Selfrlahum Base", null, Spots));
        Assert.Null(SpotResolve.DetectedName(null, "   ", Spots));
    }

    [Fact]
    public void TheUnstableHintSaysWhenTheReadsDisagree()
    {
        var decision = Resolve(null, null);

        // A full window that never agrees is not progress — "(5/5)" alone reads
        // as a finished counter and leaves the player waiting for nothing.
        var disagreeing = decision.Hint(5, 2, 5);
        Assert.Contains("keeps reading differently", disagreeing, StringComparison.Ordinal);
        Assert.Contains("best 2 of 5", disagreeing, StringComparison.Ordinal);

        Assert.Equal(
            "Pick a spot, or keep tracking until Location is stable (2/5).",
            decision.Hint(2, 0, 5));
    }
}
