namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Token-band XP/Adena read, anchored on the <c>adena</c> <em>unit</em> word
/// (the one trailing the figure), not the "Adena" section heading. Micro-crop
/// OCR of those bands is a later pass — this library only does geometry +
/// parse.
/// </summary>
public static class FarmFields
{
    public const int FigureLeftOfUnitPx = 150;
    public const int XpBandRightPadPx = 30;
    public const int DigitsLeftOfUnitPx = 160;
    public const double AdenaBandHalfHeightOfUnit = 0.8;
    public const double XpGapFallbackUnitsOfHeight = 3.75;
    public const double XpBandHalfHeightOfGap = 0.32;
    public const int XpCropPadX = 8;
    public const int XpCropPadY = 6;
    public const int AdenaFallbackWidth = 160;
    public const int AdenaFallbackAbove = 10;
    public const int AdenaFallbackBelow = 4;
    public const int EnhanceTargetHeight = 140;

    public static WordBox? PickAdenaUnit(IEnumerable<WordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words as IList<WordBox> ?? words.ToList();
        var candidates = list
            .Where(w => string.Equals(w.Text, "adena", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(w => w.Top)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var withDigitsLeft = candidates.FirstOrDefault(c => list.Any(w =>
            !ReferenceEquals(w, c)
            && w.Text.Any(char.IsAsciiDigit)
            && w.Left < c.Left
            && w.Left > c.Left - DigitsLeftOfUnitPx
            && Math.Abs(w.Top - c.Top) <= c.Height));
        return withDigitsLeft ?? candidates[0];
    }

    public static FarmTokenRead ReadTokens(IEnumerable<WordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words as IList<WordBox> ?? words.ToList();
        var unit = PickAdenaUnit(list);
        if (unit is null)
        {
            return FarmTokenRead.Empty;
        }

        var rows = LampGeometry.FindRows(list);
        var pitch = LampGeometry.RowPitch(rows);
        var leftBound = unit.Left - FigureLeftOfUnitPx;
        var rightBound = unit.Left + XpBandRightPadPx;

        var adenaTokens = Band(list, unit.Top, unit.Height * AdenaBandHalfHeightOfUnit, leftBound, unit.Left - 1);
        var adena = GameNumber.Parse(adenaTokens.Select(w => w.Text));

        var gap = pitch ?? unit.Height * XpGapFallbackUnitsOfHeight;
        var xpTokens = Band(list, unit.Top - gap, gap * XpBandHalfHeightOfGap, leftBound, rightBound);
        var xp = GameNumber.Parse(xpTokens.Select(w => w.Text));

        return new FarmTokenRead(unit, xp, adena, xpTokens, adenaTokens, pitch);
    }

    public static CropRect XpMicroCrop(IReadOnlyList<WordBox> xpTokens, int imageWidth, int imageHeight)
    {
        if (xpTokens.Count == 0)
        {
            return default;
        }

        var left = Math.Max(0, (int)Math.Round(xpTokens.Min(w => w.Left) - XpCropPadX, MidpointRounding.AwayFromZero));
        var top = Math.Max(0, (int)Math.Round(xpTokens.Min(w => w.Top) - XpCropPadY, MidpointRounding.AwayFromZero));
        var right = Math.Min(imageWidth, (int)Math.Round(xpTokens.Max(w => w.Left + w.Width) + XpCropPadX, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(imageHeight, (int)Math.Round(xpTokens.Max(w => w.Top + w.Height) + XpCropPadY, MidpointRounding.AwayFromZero));
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static CropRect AdenaFallbackCrop(WordBox unit, int imageWidth, int imageHeight)
    {
        var left = Math.Max(0, (int)Math.Round(unit.Left - AdenaFallbackWidth, MidpointRounding.AwayFromZero));
        var top = Math.Max(0, (int)Math.Round(unit.Top - AdenaFallbackAbove, MidpointRounding.AwayFromZero));
        var right = Math.Min(imageWidth, (int)Math.Round(unit.Left, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(imageHeight, (int)Math.Round(unit.Top + unit.Height + AdenaFallbackBelow, MidpointRounding.AwayFromZero));
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Pixel window of the XP token band, used when the crop pass emitted no
    /// XP tokens to micro-crop. Same geometry as <see cref="ReadTokens"/>,
    /// plus the splice pad.
    /// </summary>
    public static CropRect XpBandCrop(WordBox unit, double? pitch, int imageWidth, int imageHeight)
    {
        var gap = pitch ?? unit.Height * XpGapFallbackUnitsOfHeight;
        if (gap <= 0)
        {
            gap = unit.Height * XpGapFallbackUnitsOfHeight;
        }

        var centerTop = unit.Top - gap;
        var halfHeight = gap * XpBandHalfHeightOfGap;
        var left = Math.Max(0, (int)Math.Round(unit.Left - FigureLeftOfUnitPx - XpCropPadX, MidpointRounding.AwayFromZero));
        var top = Math.Max(0, (int)Math.Round(centerTop - halfHeight - XpCropPadY, MidpointRounding.AwayFromZero));
        var right = Math.Min(imageWidth, (int)Math.Round(unit.Left + XpBandRightPadPx + XpCropPadX, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(imageHeight, (int)Math.Round(centerTop + halfHeight + XpCropPadY, MidpointRounding.AwayFromZero));
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static List<WordBox> Band(
        IEnumerable<WordBox> words,
        double centerTop,
        double halfHeight,
        double leftBound,
        double rightBound)
        => words
            .Where(w => Math.Abs(w.Top - centerTop) <= halfHeight
                && w.Left >= leftBound
                && w.Left <= rightBound)
            .OrderBy(w => w.Left)
            .ToList();
}

public sealed record FarmTokenRead(
    WordBox? Unit,
    long? XpFromTokens,
    long? AdenaFromTokens,
    IReadOnlyList<WordBox> XpTokens,
    IReadOnlyList<WordBox> AdenaTokens,
    double? Pitch)
{
    public static FarmTokenRead Empty { get; } = new(null, null, null, [], [], null);
}
