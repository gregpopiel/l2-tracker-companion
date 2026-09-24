using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class SpotTargetTests
{
    private static readonly AreaInfo World = new(1, "World");

    private static readonly SpotInfo Dragon = new(
        10, "Dragon Valley (east)", 1, new SpotAreaInfo(1, "World"));

    private static readonly SpotInfo Aligator = new(
        184, "Aligator", 2, new SpotAreaInfo(2, "Special Zone"));

    private static readonly IReadOnlyList<SpotInfo> Spots = [Dragon, Aligator];

    private static SpotTargetDecision Decide(
        bool userChose = false,
        SpotInfo? selected = null,
        string? currentHint = null,
        string? settledName = null,
        IEnumerable<SpotInfo>? spots = null,
        bool spotsLoaded = true,
        AreaInfo? worldArea = null,
        bool worldSpecified = false,
        bool tracking = true,
        bool hasReads = true)
    {
        var world = worldSpecified ? worldArea : (worldArea ?? World);
        return SpotTarget.Decide(
            userChose,
            selected,
            currentHint,
            settledName,
            spotsLoaded ? spots ?? Spots : spots,
            spotsLoaded,
            world,
            tracking,
            hasReads);
    }

    [Fact]
    public void AnExactHitFillsAnEmptyPicker()
    {
        var decision = Decide(currentHint: "Dragon Valley (east)");

        Assert.Same(Dragon, decision.Spot);
        Assert.True(decision.CanSave);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void ALaterExactHitOnADifferentPlaceReplacesTheAutomaticSpot()
    {
        var decision = Decide(selected: Dragon, currentHint: "Aligator");

        Assert.Same(Aligator, decision.Spot);
        Assert.Equal("Spot switched to \"Aligator\".", decision.Hint);
    }

    [Fact]
    public void ALetterArtifactDoesNotReplaceTheAutomaticSpot()
    {
        var decision = Decide(selected: Dragon, currentHint: "Dxagxn Vallxy (east)");

        Assert.Same(Dragon, decision.Spot);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void AMissLeavesTheAutomaticSpotInPlace()
    {
        var decision = Decide(selected: Dragon, currentHint: "Somewhere Else");

        Assert.Same(Dragon, decision.Spot);
        Assert.True(decision.CanSave);
    }

    [Fact]
    public void AManualChoiceSticksAndSaysNothing()
    {
        var decision = Decide(userChose: true, selected: Dragon, currentHint: "Aligator");

        Assert.Same(Dragon, decision.Spot);
        Assert.True(decision.CanSave);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void ClearDropsAManualChoiceSoTheReadingSetsTheSpotAgain()
    {
        var follow = new SpotFollowState();
        follow.NoteUserChoice();

        var locked = Decide(
            userChose: follow.UserChose,
            currentHint: "Dragon Valley (east)",
            settledName: "Dragon Valley (east)");

        Assert.Null(locked.Spot);
        Assert.False(locked.CanSave);

        follow.Clear();

        var resumed = Decide(
            userChose: follow.UserChose,
            currentHint: "Dragon Valley (east)",
            settledName: "Dragon Valley (east)");

        Assert.False(follow.UserChose);
        Assert.Same(Dragon, resumed.Spot);
        Assert.True(resumed.CanSave);
    }

    [Fact]
    public void ClearButtonDropsTheManualChoiceInsteadOfLockingIt()
    {
        var body = MethodSource("void ClearSpotButton_Click");
        var clearAt = body.IndexOf("_spotFollow.Clear()", StringComparison.Ordinal);
        var suppressAt = body.IndexOf("_suppressPickerEvents = true", StringComparison.Ordinal);
        var nullAt = body.IndexOf("SpotCombo.SelectedItem = null", StringComparison.Ordinal);
        var releaseAt = body.IndexOf("_suppressPickerEvents = false", StringComparison.Ordinal);
        var refreshAt = body.IndexOf("RefreshSaveEnabled()", StringComparison.Ordinal);

        Assert.DoesNotContain("NoteUserChoice", body, StringComparison.Ordinal);
        Assert.True(clearAt >= 0);
        Assert.True(suppressAt > clearAt);
        Assert.True(nullAt > suppressAt);
        Assert.True(releaseAt > nullAt);
        Assert.True(refreshAt > releaseAt);
    }

    [Fact]
    public void SaveClearsTheSpotWithoutLockingTheNextRun()
    {
        var body = MethodSource("void ResetLocalSessionAfterSave");
        var sessionAt = body.IndexOf("_sessionStore.NewSession()", StringComparison.Ordinal);
        var resetAt = body.IndexOf("_spotFollow.Reset()", StringComparison.Ordinal);
        var suppressAt = body.IndexOf("_suppressPickerEvents = true", StringComparison.Ordinal);
        var nullAt = body.IndexOf("SpotCombo.SelectedItem = null", StringComparison.Ordinal);
        var releaseAt = body.IndexOf("_suppressPickerEvents = false", StringComparison.Ordinal);

        Assert.DoesNotContain("NoteUserChoice", body, StringComparison.Ordinal);
        Assert.True(sessionAt >= 0);
        Assert.True(resetAt > sessionAt);
        Assert.True(suppressAt > resetAt);
        Assert.True(nullAt > suppressAt);
        Assert.True(releaseAt > nullAt);
    }

    private static string MethodSource(string signature)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "L2TrackerCompanion", "MainWindow.xaml.cs");
            if (File.Exists(candidate))
            {
                var source = File.ReadAllText(candidate);
                var start = source.IndexOf(signature, StringComparison.Ordinal);
                Assert.True(start >= 0);
                var next = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
                Assert.True(next > start);
                return source[start..next];
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("MainWindow.xaml.cs");
    }

    [Fact]
    public void ASettledUnknownNameCreatesAWorldSpot()
    {
        var decision = Decide(currentHint: "Brand New Camp", settledName: "Brand New Camp");

        Assert.Null(decision.Spot);
        Assert.Equal("Brand New Camp", decision.CreateName);
        Assert.Same(World, decision.WorldArea);
        Assert.True(decision.CanSave);
        Assert.Equal("Save will create a new World spot: Brand New Camp.", decision.Hint);
    }

    [Fact]
    public void AnUnsettledNameAsksThePlayerToPickOrKeepTracking()
    {
        var decision = Decide();

        Assert.False(decision.CanSave);
        Assert.Equal(
            "Pick a spot, or keep tracking until the location name settles.",
            decision.Hint);
    }

    [Fact]
    public void AStoppedRunAsksOnlyToPick()
    {
        var decision = Decide(tracking: false);

        Assert.Equal("Pick a spot.", decision.Hint);
    }

    [Fact]
    public void NoReadYetStaysQuiet()
    {
        var decision = Decide(hasReads: false, spotsLoaded: false);

        Assert.False(decision.CanSave);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void UnloadedSpotsDoNotCreate()
    {
        var decision = Decide(settledName: "Brand New Camp", spotsLoaded: false);

        Assert.False(decision.CanSave);
        Assert.Equal("Spots have not loaded yet.", decision.Hint);
    }

    [Fact]
    public void TwoSpotsWithTheSameNameAskForAPick()
    {
        var spots = new[]
        {
            Dragon,
            new SpotInfo(11, "dragon valley (east)", 1, new SpotAreaInfo(1, "World")),
        };

        var decision = Decide(currentHint: "Dragon Valley (east)", spots: spots);

        Assert.False(decision.CanSave);
        Assert.Contains("Multiple spots match", decision.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void ASettledNameThatMatchesAnOwnedSpotDoesNotCreateAnother()
    {
        var decision = Decide(currentHint: "nope", settledName: "Dragon Valley (east)");

        Assert.Null(decision.CreateName);
        Assert.False(decision.CanSave);
    }

    [Fact]
    public void CreateIsBlockedWhenWorldIsMissing()
    {
        var decision = Decide(
            currentHint: "Brand New Camp",
            settledName: "Brand New Camp",
            worldArea: null,
            worldSpecified: true);

        Assert.False(decision.CanSave);
        Assert.Contains("World area was not found", decision.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeldFrameFromAnotherPlaceDoesNotCreate()
    {
        var decision = Decide(currentHint: "Dragon Valley (east)", settledName: "Brand New Camp");

        Assert.Same(Dragon, decision.Spot);
        Assert.Null(decision.CreateName);
        Assert.True(decision.CanSave);

        var creating = Decide(currentHint: "Old Camp", settledName: "Brand New Camp");
        Assert.Null(creating.CreateName);
        Assert.False(creating.CanSave);
        Assert.Equal("The reading to save is from another place. Pick a spot.", creating.Hint);
    }

    [Fact]
    public void AGarbledHintSelectsTheOneOwnedSpot()
    {
        var decision = Decide(
            currentHint: "Dxagxn Vallxy (east)",
            settledName: "Dragon Valley (east)");

        Assert.Same(Dragon, decision.Spot);
        Assert.Null(decision.CreateName);
        Assert.True(decision.CanSave);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void TwoFuzzyHitsAskForAPick()
    {
        var spots = new[]
        {
            Dragon,
            new SpotInfo(11, "Dragon Vallxy (east)", 1, new SpotAreaInfo(1, "World")),
        };

        var decision = Decide(
            currentHint: "Dragan Valley (east)",
            settledName: "Dragan Valley (east)",
            spots: spots);

        Assert.False(decision.CanSave);
        Assert.Contains("Multiple spots match", decision.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void OneTrailingNoteSelectsThatSpot()
    {
        var top = new SpotInfo(21, "Hot Springs Top", 1, new SpotAreaInfo(1, "World"));
        var decision = Decide(
            currentHint: "Hot Springs",
            settledName: "Hot Springs",
            spots: [top]);

        Assert.Same(top, decision.Spot);
        Assert.Null(decision.CreateName);
        Assert.True(decision.CanSave);
    }

    [Fact]
    public void TwoTrailingNotesAskForAPick()
    {
        var low = new SpotInfo(22, "Hot Springs Low", 1, new SpotAreaInfo(1, "World"));
        var spots = new[]
        {
            new SpotInfo(21, "Hot Springs Top", 1, new SpotAreaInfo(1, "World")),
            low,
        };
        var decision = Decide(
            currentHint: "Hot Springs",
            settledName: "Hot Springs",
            spots: spots);

        Assert.Same(low, decision.Spot);
        Assert.Null(decision.CreateName);
        Assert.True(decision.CanSave);
        Assert.Contains("Multiple spots match", decision.Hint, StringComparison.Ordinal);

        var again = Decide(
            selected: low,
            currentHint: "Hot Springs",
            settledName: "Hot Springs",
            spots: spots);

        Assert.Same(low, again.Spot);
        Assert.Contains("Multiple spots match", again.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnrelatedSpotStaysWhenSeveralSuffixesMatch()
    {
        var spots = new[]
        {
            new SpotInfo(21, "Hot Springs Top", 1, new SpotAreaInfo(1, "World")),
            new SpotInfo(22, "Hot Springs Low", 1, new SpotAreaInfo(1, "World")),
        };
        var decision = Decide(
            selected: Dragon,
            currentHint: "Hot Springs",
            settledName: "Hot Springs",
            spots: spots);

        Assert.Same(Dragon, decision.Spot);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void NoteUserChoiceAndReset()
    {
        var follow = new SpotFollowState();

        Assert.False(follow.UserChose);
        follow.NoteUserChoice();
        Assert.True(follow.UserChose);
        follow.Reset();
        Assert.False(follow.UserChose);
    }
}
