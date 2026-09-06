using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class SpotAutoCorrectTests
{
    [Fact]
    public void AFreshInstanceMayOverwrite()
    {
        var correct = new SpotAutoCorrect();

        Assert.True(correct.MayOverwrite);
        Assert.False(correct.NoticePending);
        Assert.False(correct.Pinned);
    }

    /// <summary>
    /// Filling an empty picker is an ordinary preselect. Treating it as a
    /// correction would raise a notice about nothing and, worse, let the very
    /// next pick pin the run — spending the player's one chance to overrule
    /// before there was anything to disagree with.
    /// </summary>
    [Fact]
    public void FillingAnEmptyPickerIsNotACorrection()
    {
        var correct = new SpotAutoCorrect();

        correct.NoteApplied(replacedAPick: false);
        Assert.False(correct.NoticePending);

        correct.NoteUserPick();
        Assert.True(correct.MayOverwrite);
    }

    [Fact]
    public void APickAfterACorrectionPinsForTheRestOfTheRun()
    {
        var correct = new SpotAutoCorrect();

        correct.NoteApplied(replacedAPick: true);
        Assert.True(correct.NoticePending);
        Assert.True(correct.MayOverwrite);

        correct.NoteUserPick();
        Assert.True(correct.Pinned);
        Assert.False(correct.MayOverwrite);
        Assert.False(correct.NoticePending);

        // And it stays pinned however many reads land afterwards.
        correct.NoteApplied(replacedAPick: true);
        Assert.False(correct.MayOverwrite);
    }

    [Fact]
    public void APickWithNoCorrectionPendingDoesNotPin()
    {
        var correct = new SpotAutoCorrect();

        correct.NoteUserPick();

        // The player picked before anything was corrected, so the first
        // correction is still allowed to overwrite it.
        Assert.True(correct.MayOverwrite);
    }

    [Fact]
    public void ResetReleasesThePin()
    {
        var correct = new SpotAutoCorrect();
        correct.NoteApplied(replacedAPick: true);
        correct.NoteUserPick();
        Assert.False(correct.MayOverwrite);

        correct.Reset();

        Assert.True(correct.MayOverwrite);
        Assert.False(correct.NoticePending);
        Assert.False(correct.Pinned);
    }

    /// <summary>
    /// The notice is news, not state: once a later read still names the spot in
    /// the picker there is nothing left to announce, and the hint slot has to go
    /// back to reporting the live location rather than a switch from long ago.
    /// </summary>
    [Fact]
    public void AConfirmingReadRetiresTheNoticeButNotTheRightToPin()
    {
        var correct = new SpotAutoCorrect();
        correct.NoteApplied(replacedAPick: true);

        correct.NoteConfirmed();
        Assert.False(correct.NoticePending);

        // The player can still answer that correction after the notice stopped
        // showing, and doing so must pin.
        correct.NoteUserPick();
        Assert.True(correct.Pinned);
    }

    /// <summary>
    /// Clear empties the picker; it must not hand back protection the player
    /// already earned, or re-picking the same spot afterwards would fail to pin
    /// and the next matching read would overwrite it a second time.
    /// </summary>
    [Fact]
    public void DismissingTheNoticeKeepsThePin()
    {
        var correct = new SpotAutoCorrect();
        correct.NoteApplied(replacedAPick: true);
        correct.NoteUserPick();
        Assert.False(correct.MayOverwrite);

        correct.DismissNotice();

        Assert.False(correct.NoticePending);
        Assert.False(correct.MayOverwrite);
    }

    [Fact]
    public void DismissingTheNoticeKeepsAnUnansweredCorrectionsRightToPin()
    {
        var correct = new SpotAutoCorrect();
        correct.NoteApplied(replacedAPick: true);

        // Clear before answering it: the notice goes, the entitlement does not.
        correct.DismissNotice();
        correct.NoteUserPick();

        Assert.True(correct.Pinned);
    }
}
