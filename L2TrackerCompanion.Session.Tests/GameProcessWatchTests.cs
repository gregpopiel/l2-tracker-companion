using L2TrackerCompanion.Session;
using Xunit;

namespace L2TrackerCompanion.Session.Tests;

public class GameProcessWatchTests
{
    private const bool Alive = true;
    private const bool Gone = false;

    [Fact]
    public void FirstSightingIsNotARestart()
    {
        var watch = new GameProcessWatch();
        Assert.False(watch.Notice(100, Alive));
        Assert.Equal(100, watch.FollowedProcessId);
    }

    [Fact]
    public void TheSameClientStayingInViewIsNotARestart()
    {
        var watch = new GameProcessWatch();
        watch.Notice(100, Alive);
        Assert.False(watch.Notice(100, Alive));
    }

    [Fact]
    public void AClientThatWentAwayAndCameBackIsARestart()
    {
        var watch = new GameProcessWatch();
        watch.Notice(100, Alive);

        Assert.False(watch.Notice(null, Gone));
        Assert.True(watch.Notice(200, Alive));
    }

    [Fact]
    public void ARestartIsReportedOnlyOnce()
    {
        var watch = new GameProcessWatch();
        watch.Notice(100, Alive);
        watch.Notice(null, Gone);
        Assert.True(watch.Notice(200, Alive));

        Assert.False(watch.Notice(200, Alive));
        Assert.False(watch.Notice(300, Alive));
    }

    [Fact]
    public void AltTabbingBetweenTwoClientsIsNotARestart()
    {
        // Both clients stay up, so no window is ever missing; the reported id
        // just follows whichever one is in front.
        var watch = new GameProcessWatch();
        watch.Notice(100, Alive);

        Assert.False(watch.Notice(200, Alive));
        Assert.False(watch.Notice(100, Alive));
        Assert.False(watch.Notice(200, Alive));
    }

    [Fact]
    public void ClosingOneOfTwoClientsIsNotARestart()
    {
        // The app was following the client that got closed, so the id it now
        // reports differs and the old process is gone — but the surviving
        // client's panel was never reset. A game window was visible the whole
        // time, which is what tells the two apart.
        var watch = new GameProcessWatch();
        watch.Notice(100, Alive);
        watch.Notice(200, Alive);

        Assert.False(watch.Notice(100, Alive));
    }

    [Fact]
    public void AWindowMissingWhileTheClientLivesIsNotAGap()
    {
        // Loading screen, or a moment with no usable window: the process is
        // still there, so nothing was restarted.
        var watch = new GameProcessWatch();
        watch.Notice(100, Alive);

        Assert.False(watch.Notice(null, Alive));
        Assert.False(watch.Notice(200, Alive));
    }
}
