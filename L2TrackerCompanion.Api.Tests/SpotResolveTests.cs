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
        Assert.Equal("Save will use existing spot: Dragon Valley (east).", decision.Hint(0, 5));
    }

    [Fact]
    public void AStableUnknownNameCreatesAWorldSpot()
    {
        var decision = Resolve(null, "Brand New Camp");

        Assert.Equal(SpotResolveKind.CreateWorld, decision.Kind);
        Assert.Equal("Brand New Camp", decision.Name);
        Assert.Same(World, decision.WorldArea);
        Assert.Equal("Save will create a new World spot: Brand New Camp.", decision.Hint(0, 5));
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
        Assert.Equal("Spots have not loaded yet.", decision.Hint(5, 5));
    }

    [Fact]
    public void NoStableHintNeedsThePicker()
    {
        var decision = Resolve(null, null);

        Assert.Equal(SpotResolveKind.Unstable, decision.Kind);
        Assert.False(decision.CanSave);
        Assert.Equal(
            "Pick a spot, or keep tracking until Location is stable (2/5).",
            decision.Hint(2, 5));
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
        Assert.Contains("Dragon Valley (east)", decision.Hint(5, 5), StringComparison.Ordinal);
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
        Assert.Contains("Multiple spots match", decision.Hint(5, 5), StringComparison.Ordinal);
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
}
