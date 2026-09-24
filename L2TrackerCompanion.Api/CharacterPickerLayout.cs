namespace L2TrackerCompanion.Api;

/// <summary>
/// One character is just its name – a dropdown for a list of one is noise.
/// Zero or several keep the picker (disabled when empty).
/// </summary>
public readonly record struct CharacterPickerLayout(
    bool ShowCombo,
    bool ComboEnabled,
    string LabelText)
{
    public static CharacterPickerLayout For(
        IReadOnlyList<CharacterInfo>? characters,
        CharacterInfo? selected)
    {
        var list = characters ?? [];
        var single = list.Count == 1;
        return new(
            ShowCombo: !single,
            ComboEnabled: list.Count > 1,
            LabelText: single ? selected?.Name ?? string.Empty : string.Empty);
    }
}
