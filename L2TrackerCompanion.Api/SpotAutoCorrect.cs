namespace L2TrackerCompanion.Api;

/// <summary>
/// Whether a detected location may overwrite the spot already in the picker.
/// </summary>
/// <remarks>
/// A hint that exactly names an owned spot outranks a pick that may be stale,
/// so the first correction is allowed to overwrite one. But the player has to
/// be able to win: a pick made <em>in response</em> to a correction pins the
/// session where they put it for the rest of the run. Without that, the next
/// tick would undo their choice within seconds and they could never file a
/// session anywhere but where they happen to be standing.
///
/// The notice and the right to pin are tracked apart on purpose. They look
/// like one flag and are not: the notice is transient (it stops being news
/// once a later read confirms the picker), while having been corrected is what
/// entitles the player to pin, and that entitlement has to outlive both the
/// notice and a trip through <b>Clear</b>.
///
/// Lives here rather than as flags on the window so the one genuinely stateful
/// piece of this behaviour can be tested — the WPF project has no test project
/// of its own.
/// </remarks>
public sealed class SpotAutoCorrect
{
    /// <summary>
    /// A correction moved the picker and no later read has confirmed it yet.
    /// Drives the notice under the Spot field.
    /// </summary>
    public bool NoticePending { get; private set; }

    /// <summary>
    /// A correction has happened this run, so the player's next pick is an
    /// answer to it and pins the session. Outlives <see cref="NoticePending"/>.
    /// </summary>
    private bool _corrected;

    /// <summary>The player overruled a correction; stop overwriting.</summary>
    public bool Pinned { get; private set; }

    public bool MayOverwrite => !Pinned;

    /// <param name="replacedAPick">
    /// False when the picker was empty. Filling an empty picker is an ordinary
    /// preselect, not a correction, so it must neither raise the notice nor
    /// entitle the next pick to pin — otherwise an ordinary session start would
    /// spend the player's one chance to overrule before they had anything to
    /// disagree with.
    /// </param>
    public void NoteApplied(bool replacedAPick)
    {
        NoticePending = replacedAPick;
        if (replacedAPick)
        {
            _corrected = true;
        }
    }

    /// <summary>
    /// A later read still names the spot in the picker, so the switch is no
    /// longer news and the hint slot goes back to reporting the live location.
    /// </summary>
    /// <remarks>
    /// Deliberately does not touch the right to pin: the player may answer a
    /// correction long after the notice stopped being shown.
    /// </remarks>
    public void NoteConfirmed() => NoticePending = false;

    /// <summary>
    /// Take the notice down without giving anything else up — what <b>Clear</b>
    /// needs, since emptying the picker must not revoke protection the player
    /// already earned.
    /// </summary>
    public void DismissNotice() => NoticePending = false;

    /// <summary>The player chose a spot themselves.</summary>
    public void NoteUserPick()
    {
        if (_corrected)
        {
            Pinned = true;
        }

        _corrected = false;
        NoticePending = false;
    }

    /// <summary>
    /// Back to square one: a new spot list, a new character, a signed-out
    /// window. Only for a change of vocabulary — <b>Clear</b> uses
    /// <see cref="DismissNotice"/>.
    /// </summary>
    public void Reset()
    {
        NoticePending = false;
        _corrected = false;
        Pinned = false;
    }
}
