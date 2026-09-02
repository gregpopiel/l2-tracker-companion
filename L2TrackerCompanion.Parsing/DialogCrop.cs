namespace L2TrackerCompanion.Parsing;

/// <summary>
/// One OCR token with a bounding box in source-image pixels, origin top-left.
/// Deliberately not a WinRT type — locate/crop geometry is unit-testable.
/// </summary>
public sealed record WordBox(string Text, double Left, double Top, double Width, double Height);

/// <summary>
/// Dialog-locate hit: the "Play Report" subtitle if present, otherwise the
/// topmost "Characters" title. Kind is <c>Report</c> or <c>Characters</c>.
/// </summary>
public sealed record DialogAnchor(WordBox Word, string Kind);

/// <summary>
/// Integer pixel rectangle, already clamped to the source image.
/// </summary>
public readonly record struct CropRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Locate the Characters / Play Report dialog from word boxes and crop to a
/// generous fixed-pixel margin around that anchor. Margins are NOT a multiple
/// of the anchor's glyph height — Tesseract (and WinOCR) report that height
/// noisily (9–32px for the same word) while the game draws this dialog at a
/// fixed pixel size. Left and right are equal because the Magic Lamp table
/// docks to either side of the Characters window; 350/550 clipped a
/// left-docked table that needed 369px.
/// </summary>
public static class DialogCrop
{
    public const int MarginLeft = 550;
    public const int MarginRight = 550;
    public const int MarginTop = 80;
    public const int MarginBottom = 550;

    public static DialogAnchor? FindAnchor(IEnumerable<WordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words as IList<WordBox> ?? words.ToList();

        var characters = list
            .Where(w => string.Equals(w.Text, "Characters", StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Top)
            .FirstOrDefault();
        var report = list
            .Where(w => string.Equals(w.Text, "Report", StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Top)
            .FirstOrDefault();

        // The real subtitle sits just under the title (~50–60px in the 41-set).
        // Windows.Media.Ocr also emits a lowercase "report" from chat on full-
        // desktop captures, hundreds of pixels away — preferring that over
        // Characters placed the crop in the chat log and missed the dialog.
        if (report is not null && (characters is null || IsPlayReportSubtitle(report, characters)))
        {
            return new DialogAnchor(report, "Report");
        }

        return characters is null ? null : new DialogAnchor(characters, "Characters");
    }

    /// <summary>
    /// Play Report is the line immediately under the Characters title, not a
    /// same-spelling token elsewhere in the frame.
    /// </summary>
    public static bool IsPlayReportSubtitle(WordBox report, WordBox characters)
    {
        var dy = report.Top - characters.Top;
        return dy >= -10 && dy <= 120;
    }

    public static CropRect Rect(WordBox anchor, int imageWidth, int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(imageHeight);

        var left = ClampRound(anchor.Left - MarginLeft, 0, imageWidth);
        var top = ClampRound(anchor.Top - MarginTop, 0, imageHeight);
        var right = ClampRound(anchor.Left + MarginRight, 0, imageWidth);
        var bottom = ClampRound(anchor.Top + MarginBottom, 0, imageHeight);
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static CropRect Rect(DialogAnchor anchor, int imageWidth, int imageHeight)
        => Rect(anchor.Word, imageWidth, imageHeight);

    private static int ClampRound(double value, int min, int max)
    {
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded < min)
        {
            return min;
        }

        return rounded > max ? max : rounded;
    }
}
