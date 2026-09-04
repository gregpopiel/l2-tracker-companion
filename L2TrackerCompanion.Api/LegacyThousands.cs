namespace L2TrackerCompanion.Api;

/// <summary>
/// TEMPORARY. The website stores — and this API returns — farm amounts in
/// thousands, so every figure derived from them is a thousandth of what the
/// game's own panel prints. Anything comparing an API figure against a live
/// Play Report read has to undo that first, or the two sides sit three
/// orders of magnitude apart and the comparison silently reads as a landslide.
/// </summary>
/// <remarks>
/// This is the read half of the convention whose write half is
/// <c>L2TrackerCompanion.Parsing.Amounts.ToThousands</c>. Both exist only until
/// the project drops the ×1000 storage convention; when it does, delete this
/// class and its single call site in <see cref="SpotBenchmark"/> together.
/// </remarks>
public static class LegacyThousands
{
    public const int Factor = 1000;

    /// <summary>
    /// An API amount (thousands) as the raw figure the game prints.
    /// Null in, null out — an unfarmed spot has no average to scale.
    /// </summary>
    public static long? ToRaw(long? stored) => stored is null ? null : stored.Value * Factor;
}
