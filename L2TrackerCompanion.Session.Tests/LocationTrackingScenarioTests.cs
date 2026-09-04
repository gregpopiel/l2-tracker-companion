using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Session.Tests;

/// <summary>
/// End-to-end reproductions of the live poll loop
/// (<c>LocationStability.Evaluate</c> over the whole session's raw minimap
/// hints, feeding the settled name into <c>LocationChangeWatch.Notice</c>) —
/// the pipeline MainWindow runs on every 10s tick. These pin down exactly
/// when a spot change gets noticed, so a regression in either piece (or in
/// how they're wired together) shows up here instead of only in manual play.
/// </summary>
public class LocationTrackingScenarioTests
{
    /// <summary>
    /// Mirrors MainWindow's <c>ShowLocationChange</c>: re-evaluate stability
    /// over the *entire* raw hint history so far, then notice against the
    /// settled name (or null while unsettled).
    /// </summary>
    private sealed class PollLoop
    {
        private readonly List<string?> _rawHints = [];
        private readonly LocationChangeWatch _watch = new();

        public string? Tick(string? rawHint)
        {
            _rawHints.Add(rawHint);
            var stability = LocationStability.Evaluate(_rawHints);
            return _watch.Notice(stability.IsStable ? stability.CanonicalName : null);
        }
    }

    [Fact]
    public void StayingAtOneSpotForTheWholeSessionNeverWarns()
    {
        var loop = new PollLoop();

        for (var i = 0; i < 10; i++)
        {
            Assert.Null(loop.Tick("Dragon Valley"));
        }
    }

    [Fact]
    public void MovingSpotsOnlyWarnsOnceTheNewLocationHasSettled()
    {
        var loop = new PollLoop();

        // Tracking starts at Dragon Valley and settles (first sighting).
        for (var i = 0; i < 5; i++)
        {
            Assert.Null(loop.Tick("Dragon Valley"));
        }

        // Player walks to Training Zone. The rolling 5-read window still has
        // a Dragon Valley majority for the first few ticks, so this must
        // stay quiet — an early, unsettled read is not a reported move.
        Assert.Null(loop.Tick("Training Zone")); // window: D D D D T
        Assert.Null(loop.Tick("Training Zone")); // window: D D D T T (3/5, not settled)
        Assert.Null(loop.Tick("Training Zone")); // window: D D T T T (3/5, still not settled)

        // The 4th consecutive Training Zone read finally gives the window a
        // 4/5 majority — this is the tick that must warn.
        var warning = loop.Tick("Training Zone"); // window: D T T T T
        Assert.NotNull(warning);
        Assert.Contains("Training Zone", warning, StringComparison.Ordinal);

        // Once settled, further reads at the same place stay quiet.
        Assert.Null(loop.Tick("Training Zone"));
        Assert.Null(loop.Tick("Training Zone"));
    }

    [Fact]
    public void ABriefMinimapOcclusionDuringTheMoveDoesNotResetOrDoubleReportTheChange()
    {
        var loop = new PollLoop();

        for (var i = 0; i < 5; i++)
        {
            loop.Tick("Dragon Valley");
        }

        loop.Tick("Training Zone");
        loop.Tick(null); // occluded minimap frame — a gap, not a move
        loop.Tick("   ");
        loop.Tick("Training Zone");
        loop.Tick("Training Zone");
        var warning = loop.Tick("Training Zone");

        Assert.NotNull(warning);
        Assert.Contains("Training Zone", warning, StringComparison.Ordinal);

        // No second notice for the same settled location.
        Assert.Null(loop.Tick("Training Zone"));
    }

    [Fact]
    public void WalkingBackToTheOriginalSpotIsReportedAsAMoveAgain()
    {
        var loop = new PollLoop();

        for (var i = 0; i < 5; i++)
        {
            loop.Tick("Dragon Valley");
        }

        for (var i = 0; i < 4; i++)
        {
            loop.Tick("Training Zone");
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.Null(loop.Tick("Dragon Valley"));
        }

        var warning = loop.Tick("Dragon Valley");
        Assert.NotNull(warning);
        Assert.Contains("Dragon Valley", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartingTheGameForgetsWhereTheOldSessionWasSoTheFirstReadIsNotAMove()
    {
        var loop = new PollLoop();

        for (var i = 0; i < 5; i++)
        {
            loop.Tick("Dragon Valley");
        }

        // Equivalent to MainWindow's HideLocationChange()/watch.Reset() on a
        // detected game restart: the new session's history is separate.
        var freshLoop = new PollLoop();
        for (var i = 0; i < 4; i++)
        {
            Assert.Null(freshLoop.Tick("Training Zone"));
        }

        Assert.Null(freshLoop.Tick("Training Zone"));
    }
}
