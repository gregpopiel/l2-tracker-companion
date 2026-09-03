using L2TrackerCompanion.Session;
using Xunit;

namespace L2TrackerCompanion.Session.Tests;

public class LocationChangeWatchTests
{
    [Fact]
    public void FirstSettledLocationIsNotAMove()
    {
        var watch = new LocationChangeWatch();

        Assert.Null(watch.Notice("Cruma Tower"));
        Assert.Equal("Cruma Tower", watch.Current);
    }

    [Fact]
    public void StayingPutSaysNothing()
    {
        var watch = new LocationChangeWatch();
        watch.Notice("Cruma Tower");

        Assert.Null(watch.Notice("Cruma Tower"));
        Assert.Null(watch.Notice("cruma tower"));
        Assert.Null(watch.Notice("  Cruma Tower  "));
    }

    [Fact]
    public void MovingReportsOnceAndThenGoesQuiet()
    {
        var watch = new LocationChangeWatch();
        watch.Notice("Cruma Tower");

        var message = watch.Notice("Blazing Swamp");
        Assert.NotNull(message);
        Assert.Contains("Blazing Swamp", message, StringComparison.Ordinal);
        Assert.Contains("Play Report", message, StringComparison.Ordinal);

        Assert.Null(watch.Notice("Blazing Swamp"));
        Assert.Equal("Blazing Swamp", watch.Current);
    }

    [Fact]
    public void AnUnsettledStretchIsAGapNotAMove()
    {
        var watch = new LocationChangeWatch();
        watch.Notice("Cruma Tower");

        // LocationStability passes null while its window disagrees — an
        // occluded minimap must not read as having walked somewhere.
        Assert.Null(watch.Notice(null));
        Assert.Null(watch.Notice("   "));
        Assert.Null(watch.Notice("Cruma Tower"));
        Assert.Equal("Cruma Tower", watch.Current);
    }

    [Fact]
    public void MovingBackIsStillAMove()
    {
        var watch = new LocationChangeWatch();
        watch.Notice("Cruma Tower");
        watch.Notice("Blazing Swamp");

        Assert.NotNull(watch.Notice("Cruma Tower"));
    }

    [Fact]
    public void ResetMakesTheNextLocationAFirstSightingAgain()
    {
        var watch = new LocationChangeWatch();
        watch.Notice("Cruma Tower");

        watch.Reset();
        Assert.Null(watch.Current);
        Assert.Null(watch.Notice("Blazing Swamp"));
    }
}
