namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Locate Magic Lamp colour-name anchors and the row pitch between them.
/// Pitch, not glyph height, is what the XP band (and later the table crop)
/// is measured from — box height for the same word jumps 9–32px.
/// </summary>
public static class LampGeometry
{
    public static readonly string[] Colors = ["red", "purple", "blue", "green"];

    /// <summary>
    /// Colour names sit in one vertical column. WinOCR also emits look-alikes
    /// elsewhere in the dialog ("red" out of "Acquired") that are taller than
    /// the real name; taking the global tallest box then makes pitch negative
    /// and the table crop disappears. Keep the densest x-cluster, then the
    /// tallest box per colour inside it.
    /// </summary>
    public static IReadOnlyDictionary<string, WordBox> FindRows(IEnumerable<WordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words as IList<WordBox> ?? words.ToList();
        var hits = new List<(string Color, WordBox Box)>();
        foreach (var word in list)
        {
            foreach (var color in Colors)
            {
                if (IsColorName(word.Text, color))
                {
                    hits.Add((color, word));
                }
            }
        }

        if (hits.Count == 0)
        {
            return new Dictionary<string, WordBox>(StringComparer.Ordinal);
        }

        var ordered = hits.OrderBy(h => h.Box.Left).ToList();
        var clusters = new List<List<(string Color, WordBox Box)>>();
        foreach (var hit in ordered)
        {
            if (clusters.Count == 0
                || hit.Box.Left - clusters[^1][^1].Box.Left > ColorColumnXSlackPx)
            {
                clusters.Add([hit]);
            }
            else
            {
                clusters[^1].Add(hit);
            }
        }

        var best = clusters
            .OrderByDescending(c => c.Select(h => h.Color).Distinct(StringComparer.Ordinal).Count())
            .ThenByDescending(c => c.Count)
            .ThenByDescending(c => c.Average(h => h.Box.Left))
            .First();

        var rows = new Dictionary<string, WordBox>(StringComparer.Ordinal);
        foreach (var color in Colors)
        {
            var colorHits = best.Where(h => h.Color == color).Select(h => h.Box).ToList();
            if (colorHits.Count == 0)
            {
                continue;
            }

            rows[color] = colorHits.Aggregate((a, b) => b.Height > a.Height ? b : a);
        }

        return rows;
    }

    public static double? RowPitch(IReadOnlyDictionary<string, WordBox> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var tops = new List<(double Top, int Index)>();
        for (var i = 0; i < Colors.Length; i++)
        {
            if (rows.TryGetValue(Colors[i], out var box))
            {
                tops.Add((box.Top, i));
            }
        }

        if (tops.Count < 2)
        {
            return null;
        }

        var gaps = new List<double>();
        for (var i = 1; i < tops.Count; i++)
        {
            var gap = (tops[i].Top - tops[i - 1].Top) / (tops[i].Index - tops[i - 1].Index);
            // A colour-name false hit above the table (WinOCR reading "red"
            // out of "Acquired") makes this negative and would place the XP
            // band below Adena. Impossible row spacing is missing pitch.
            if (gap > 0)
            {
                gaps.Add(gap);
            }
        }

        return gaps.Count == 0 ? null : Median(gaps);
    }

    public const int TableLeftOfNamePx = 80;

    public const int TableRightOfNamePx = 268;

    public const double TableAboveFirstPitch = 0.8;

    public const double TableBelowLastPitch = 1.1;

    public const int TableScale = 3;

    public const int RowXpDxMin = 150;

    public const int RowXpDxMax = 250;

    public const int RowXpCropDxStart = 188;

    public const int RowXpCropDxEnd = 252;

    public const double RowXpCropTopPitch = 0.18;

    public const double RowXpCropHeightPitch = 0.56;

    public const int RowXpEnhanceTargetHeight = 120;

    public const double FallbackPitchUnitsOfNameHeight = 4.15;

    /// <summary>
    /// Lamp names in a column share an x to within a few pixels. Wider than
    /// this is a different hit (the "red" inside Acquired, ~150px left).
    /// </summary>
    public const double ColorColumnXSlackPx = 40;

    /// <summary>
    /// WinOCR often reads Blue as <c>gue</c>/<c>Nue</c>/<c>ue</c> (step 5)
    /// and Green as <c>G-reen</c>. Those aliases map to the row so
    /// all-or-none isn't lost for a look-alike.
    /// </summary>
    public static bool IsColorName(string text, string color)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(color);
        var folded = text.Replace("-", "", StringComparison.Ordinal);
        if (string.Equals(folded, color, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return color == "blue"
            && (string.Equals(folded, "gue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folded, "nue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folded, "ue", StringComparison.OrdinalIgnoreCase));
    }

    public static double EffectivePitch(double? pitch, WordBox anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return pitch is > 0 ? pitch.Value : FallbackPitchUnitsOfNameHeight * Math.Max(anchor.Height, 1);
    }

    /// <summary>
    /// Dialog-pixel table strip measured from colour-name anchors + row pitch,
    /// then upscaled <see cref="TableScale"/>×. Equal left/right isn't the
    /// point here — the icon column is ~80px left of the name, the XP column
    /// ends ~250px right.
    /// </summary>
    public static CropRect TableCrop(
        IReadOnlyDictionary<string, WordBox> rows,
        double pitch,
        int imageWidth,
        int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0 || pitch <= 0)
        {
            return default;
        }

        var indexed = new List<(int Index, WordBox Box)>();
        for (var i = 0; i < Colors.Length; i++)
        {
            if (rows.TryGetValue(Colors[i], out var box))
            {
                indexed.Add((i, box));
            }
        }

        if (indexed.Count == 0)
        {
            return default;
        }

        var first = indexed[0];
        var last = indexed[^1];
        var left = Math.Max(0, (int)Math.Round(indexed.Min(a => a.Box.Left) - TableLeftOfNamePx, MidpointRounding.AwayFromZero));
        var top = Math.Max(
            0,
            (int)Math.Round(
                first.Box.Top - (first.Index * pitch) - (TableAboveFirstPitch * pitch),
                MidpointRounding.AwayFromZero));
        var right = Math.Min(
            imageWidth,
            (int)Math.Round(indexed.Max(a => a.Box.Left) + TableRightOfNamePx, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(
            imageHeight,
            (int)Math.Round(
                last.Box.Top
                    + ((Colors.Length - 1 - last.Index) * pitch)
                    + (TableBelowLastPitch * pitch),
                MidpointRounding.AwayFromZero));
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static IReadOnlyList<WordBox> RowXpTokens(
        IEnumerable<WordBox> words,
        WordBox anchor,
        double pitch,
        double pxScale = 1)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(anchor);
        if (pitch <= 0)
        {
            return [];
        }

        return words
            .Where(w => !ReferenceEquals(w, anchor)
                && w.Left - anchor.Left >= RowXpDxMin * pxScale
                && w.Left - anchor.Left <= RowXpDxMax * pxScale
                && w.Top > anchor.Top
                && w.Top < anchor.Top + (pitch * 0.8))
            .OrderBy(w => w.Left)
            .ToList();
    }

    public static CropRect RowXpCellCrop(
        WordBox anchor,
        double pitch,
        double pxScale,
        int imageWidth,
        int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (pitch <= 0 || pxScale <= 0)
        {
            return default;
        }

        var left = Math.Max(0, (int)Math.Round(anchor.Left + (RowXpCropDxStart * pxScale), MidpointRounding.AwayFromZero));
        var top = Math.Max(0, (int)Math.Round(anchor.Top + (RowXpCropTopPitch * pitch), MidpointRounding.AwayFromZero));
        var width = (int)Math.Round((RowXpCropDxEnd - RowXpCropDxStart) * pxScale, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(RowXpCropHeightPitch * pitch, MidpointRounding.AwayFromZero);
        var right = Math.Min(imageWidth, left + width);
        var bottom = Math.Min(imageHeight, top + height);
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
    }
}
