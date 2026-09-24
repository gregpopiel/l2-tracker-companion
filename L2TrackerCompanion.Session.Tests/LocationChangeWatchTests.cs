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

        // LocationStability passes null while its window disagrees – an
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

    [Fact]
    public void AnOcrGarbleOfTheSettledZoneIsNotReportedAsAMove()
    {
        var watch = new LocationChangeWatch();
        watch.Notice("Dragon Valley (east)");

        Assert.Null(watch.Notice("prägon Villey (east)"));
    }

    [Fact]
    public void AGarbleDoesNotBecomeTheNameFutureReadsAreComparedAgainst()
    {
        var watch = new LocationChangeWatch();
        watch.Notice("Dragon Valley (east)");
        watch.Notice("prägon Villey (east)");

        Assert.Equal("Dragon Valley (east)", watch.Current);
        Assert.Null(watch.Notice("Dragon Valley (east)"));

        var message = watch.Notice("Cruma Tower");
        Assert.NotNull(message);
        Assert.Contains("Cruma Tower", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMoveNoticeStopsShowingOnceItHasHadTimeToBeRead()
    {
        var watch = new LocationChangeWatch();
        var start = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        watch.Notice("Cruma Tower", start);

        var message = watch.Notice("Blazing Swamp", start);
        Assert.NotNull(message);
        Assert.Equal(message, watch.PendingNotice(start.Add(LocationChangeWatch.NoticeLifetime).AddTicks(-1)));
        Assert.Null(watch.PendingNotice(start.Add(LocationChangeWatch.NoticeLifetime)));
    }

    [Fact]
    public void ASecondMoveReplacesThePendingNoticeAndRestartsItsClock()
    {
        var watch = new LocationChangeWatch();
        var start = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        watch.Notice("Cruma Tower", start);
        watch.Notice("Blazing Swamp", start);

        var second = start.AddMinutes(1);
        var message = watch.Notice("Dragon Valley", second);
        Assert.Contains("Dragon Valley", message, StringComparison.Ordinal);
        Assert.Equal(message, watch.PendingNotice(second.Add(LocationChangeWatch.NoticeLifetime).AddTicks(-1)));
        Assert.Null(watch.PendingNotice(second.Add(LocationChangeWatch.NoticeLifetime)));
    }

    [Fact]
    public void ResetClearsAPendingNoticeAsWellAsTheCurrentLocation()
    {
        var watch = new LocationChangeWatch();
        var start = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        watch.Notice("Cruma Tower", start);
        watch.Notice("Blazing Swamp", start);

        watch.Reset();

        Assert.Null(watch.Current);
        Assert.Null(watch.PendingNotice(start));
    }
}
