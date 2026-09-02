namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Locate Magic Lamp colour-name anchors and the row pitch between them.
/// Pitch, not glyph height, is what the XP band (and later the table crop)
/// is measured from — box height for the same word jumps 9–32px.
/// </summary>
public static class LampGeometry
{
    public static readonly string[] Colors = ["red", "purple", "blue", "green"];

    public static IReadOnlyDictionary<string, WordBox> FindRows(IEnumerable<WordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words as IList<WordBox> ?? words.ToList();
        var rows = new Dictionary<string, WordBox>(StringComparer.Ordinal);
        foreach (var color in Colors)
        {
            var hits = list
                .Where(w => string.Equals(w.Text, color, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (hits.Count == 0)
            {
                continue;
            }

            rows[color] = hits.Aggregate((best, w) => w.Height > best.Height ? w : best);
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

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
    }
}
