namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Fold OCR look-alike letters into the digits they were meant to be.
/// A digits-only character whitelist is a trap: it silently deletes the
/// letters the engine confused digits <em>for</em> (a <c>0</c> read as
/// <c>O</c> vanishes, leaving nothing to parse). Fold after the fact instead.
/// </summary>
/// <remarks>
/// Trailing <c>B</c>/<c>M</c>/<c>K</c> are magnitude suffixes, not digits —
/// <see cref="GameNumber"/> splits those off before folding so a leading
/// <c>B</c> ("B50K" for "850K") can still become 8, while a trailing <c>B</c>
/// stays billions.
/// </remarks>
public static class DigitFold
{
    public static char Apply(char ch) => ch switch
    {
        'O' or 'o' => '0',
        'I' or 'l' or 'i' => '1',
        'S' or 's' => '5',
        'B' => '8',
        'b' => '6',
        'G' => '6',
        'Z' or 'z' => '2',
        'T' or 't' => '7',
        'g' or 'q' => '9',
        _ => ch,
    };

    public static string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = Apply(source[i]);
            }
        });
    }

    /// <summary>
    /// Same map as <see cref="Apply(string)"/> except <c>B</c> is left alone,
    /// so a figure line can still carry a billions suffix. Lowercase <c>b</c>
    /// still folds to 6.
    /// </summary>
    public static string ApplyExceptB(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var ch = source[i];
                span[i] = ch == 'B' ? ch : Apply(ch);
            }
        });
    }
}
