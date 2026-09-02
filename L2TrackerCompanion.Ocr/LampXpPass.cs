using System.Globalization;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Step 11: lamp-table XP. Table crop from row-name anchors + row pitch,
/// upscale 3×, re-locate rows, read the four XP cells. All-or-none. Sum
/// must not exceed dialog XP. Closed panel ≠ failed read.
/// </summary>
public static class LampXpPass
{
    public const string DefaultLampsFolderName = "ocr-poc-lamps";

    public static string GetDefaultLampsDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OcrWordDump.AppDataFolderName,
            DefaultLampsFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static async Task<LampXpResult> RunFileAsync(
        string imagePath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return Fail($"PNG not found: {imagePath}");
        }

        try
        {
            using var dialog = await DialogCropPass.RecognizeAsync(imagePath, cancellationToken)
                .ConfigureAwait(false);
            if (!dialog.Success || dialog.CropBitmap is null || dialog.Engine is null)
            {
                return Fail(dialog.ErrorMessage ?? "Dialog crop failed");
            }

            var cropWidth = dialog.CropBitmap.PixelWidth;
            var cropHeight = dialog.CropBitmap.PixelHeight;
            var dialogBoxes = OcrRecognize.ToWordBoxes(dialog.CropWords);
            var dialogRows = LampGeometry.FindRows(dialogBoxes);
            var dialogPitch = LampGeometry.RowPitch(dialogRows);

            var (dialogXp, dialogAdena) = await ReadFarmAmountsAsync(
                    dialog, dialogBoxes, outputDirectory, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<WordBox> tableBoxes = [];
            IReadOnlyDictionary<string, WordBox> tableRows = new Dictionary<string, WordBox>();
            double? tablePitch = null;
            string? tablePngPath = null;
            var tableCrop = LampGeometry.TableCrop(dialogRows, dialogPitch ?? 0, cropWidth, cropHeight);

            SoftwareBitmap? tableBitmap = null;
            try
            {
                if (!tableCrop.IsEmpty)
                {
                    tableBitmap = await ImageEnhance.ScaleCropAsync(
                            dialog.CropBitmap,
                            tableCrop,
                            LampGeometry.TableScale,
                            cancellationToken)
                        .ConfigureAwait(false);
                    Directory.CreateDirectory(outputDirectory);
                    tablePngPath = Path.GetFullPath(Path.Combine(
                        outputDirectory,
                        Path.GetFileNameWithoutExtension(imagePath) + "_table.png"));
                    await OcrRecognize.SavePngAsync(tableBitmap, tablePngPath, cancellationToken)
                        .ConfigureAwait(false);

                    var tableRecognized = await dialog.Engine.RecognizeAsync(tableBitmap)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    tableBoxes = OcrRecognize.ToWordBoxes(OcrRecognize.ToWords(tableRecognized));
                    tableRows = LampGeometry.FindRows(tableBoxes);
                    tablePitch = LampGeometry.RowPitch(tableRows);
                }

                var parsed = new Dictionary<string, long?>(StringComparer.Ordinal);
                foreach (var color in LampGeometry.Colors)
                {
                    parsed[color] = await ReadRowXpAsync(
                            color,
                            dialog.CropBitmap,
                            dialog.Engine,
                            dialogBoxes,
                            dialogRows,
                            dialogPitch,
                            tableBitmap,
                            tableBoxes,
                            tableRows,
                            tablePitch,
                            cropWidth,
                            cropHeight,
                            outputDirectory,
                            Path.GetFileNameWithoutExtension(imagePath),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                var decision = LampXp.Decide(parsed, dialogRows, dialogXp, dialogAdena);

                Directory.CreateDirectory(outputDirectory);
                var dumpPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(imagePath) + ".txt");
                var result = new LampXpResult
                {
                    Success = true,
                    SourcePath = Path.GetFullPath(imagePath),
                    DumpPath = Path.GetFullPath(dumpPath),
                    TablePngPath = tablePngPath,
                    ImageWidth = dialog.ImageWidth,
                    ImageHeight = dialog.ImageHeight,
                    DialogCrop = dialog.Crop,
                    TableCrop = tableCrop,
                    AnchorKind = dialog.AnchorKind,
                    DialogXp = dialogXp,
                    DialogAdena = dialogAdena,
                    LampXpRead = decision.LampXpRead,
                    LampPanelClosed = decision.LampPanelClosed,
                    ExceedsDialogXp = decision.ExceedsDialogXp,
                    LampXpTotal = decision.LampXpTotal,
                    Red = decision.Red,
                    Purple = decision.Purple,
                    Blue = decision.Blue,
                    Green = decision.Green,
                    DialogColors = dialogRows.Keys.ToArray(),
                    TableColors = tableRows.Keys.ToArray(),
                };

                await File.WriteAllTextAsync(dumpPath, FormatDump(result, parsed), Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                return result;
            }
            finally
            {
                tableBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static string FormatStatus(LampXpResult result)
    {
        if (!result.Success)
        {
            return $"Lamp XP failed: {result.ErrorMessage}";
        }

        var state = result.LampPanelClosed ? "closed"
            : result.ExceedsDialogXp ? "discarded"
            : result.LampXpRead ? "read"
            : "incomplete";
        return $"lamps={state}  xp={FormatAmount(result.DialogXp)}  "
            + $"R={FormatAmount(result.Red)} P={FormatAmount(result.Purple)} "
            + $"B={FormatAmount(result.Blue)} G={FormatAmount(result.Green)}  "
            + $"dialogColors={string.Join(",", result.DialogColors)}";
    }

    public static string FormatBatchSummary(IReadOnlyList<LampXpResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr lamp table XP — 3× crop, all-or-none, sum gate");
        builder.AppendLine($"# files: {results.Count}");
        builder.AppendLine($"# succeeded: {results.Count(r => r.Success)}");
        builder.AppendLine($"# lampXpRead: {results.Count(r => r.LampXpRead)}/{results.Count}");
        builder.AppendLine($"# lampPanelClosed: {results.Count(r => r.LampPanelClosed)}/{results.Count}");
        builder.AppendLine($"# exceeds dialog XP: {results.Count(r => r.ExceedsDialogXp)}/{results.Count}");
        builder.AppendLine("file\tkind\tread\tclosed\texceeds\txp\tred\tpurple\tblue\tgreen\tdialog_colors\ttable_colors");
        foreach (var result in results)
        {
            var name = result.SourcePath is null ? "" : Path.GetFileName(result.SourcePath);
            builder.Append(Sanitize(name));
            builder.Append('\t');
            builder.Append(result.Success ? result.FrameKind : "fail");
            builder.Append('\t');
            builder.Append(YesNo(result.LampXpRead));
            builder.Append('\t');
            builder.Append(YesNo(result.LampPanelClosed));
            builder.Append('\t');
            builder.Append(YesNo(result.ExceedsDialogXp));
            builder.Append('\t');
            builder.Append(FormatAmount(result.DialogXp));
            builder.Append('\t');
            builder.Append(FormatAmount(result.Red));
            builder.Append('\t');
            builder.Append(FormatAmount(result.Purple));
            builder.Append('\t');
            builder.Append(FormatAmount(result.Blue));
            builder.Append('\t');
            builder.Append(FormatAmount(result.Green));
            builder.Append('\t');
            builder.Append(string.Join(",", result.DialogColors));
            builder.Append('\t');
            builder.AppendLine(string.Join(",", result.TableColors));
        }

        return builder.ToString();
    }

    public static string FormatBatchStatus(IReadOnlyList<LampXpResult> results)
    {
        var read = results.Count(r => r.LampXpRead);
        var closed = results.Count(r => r.LampPanelClosed);
        var exceeds = results.Count(r => r.ExceedsDialogXp);
        var open = results.Count(r => r.Success && !r.LampPanelClosed);
        return $"Lamp batch: read {read}/{open} open-panel, closed {closed}, discarded {exceeds}.";
    }

    public static string FormatBaselineComparison(
        IReadOnlyList<LampXpResult> results,
        IReadOnlyDictionary<string, LampXpBaselineRow> baseline)
    {
        var readMatch = 0;
        var closedMatch = 0;
        var compared = 0;
        var oursReadOpen = 0;
        var theirsReadOpen = 0;
        var mismatches = new List<string>();

        foreach (var result in results)
        {
            var name = result.SourcePath is null ? "" : Path.GetFileName(result.SourcePath);
            if (!baseline.TryGetValue(name, out var expected))
            {
                mismatches.Add($"{name}\t(no tesseract baseline row)");
                continue;
            }

            compared++;
            if (result.LampXpRead == expected.Read)
            {
                readMatch++;
            }

            if (result.LampPanelClosed == expected.Closed)
            {
                closedMatch++;
            }

            if (!expected.Closed && result.LampXpRead)
            {
                oursReadOpen++;
            }

            if (!expected.Closed && expected.Read)
            {
                theirsReadOpen++;
            }

            var valueMismatch = expected.Read && result.LampXpRead
                && (result.Red != expected.Red
                    || result.Purple != expected.Purple
                    || result.Blue != expected.Blue
                    || result.Green != expected.Green);
            var flagMismatch = result.LampXpRead != expected.Read
                || result.LampPanelClosed != expected.Closed;

            if (flagMismatch || valueMismatch)
            {
                mismatches.Add(
                    $"{name}\tours read={YesNo(result.LampXpRead)} closed={YesNo(result.LampPanelClosed)}"
                    + $" exceeds={YesNo(result.ExceedsDialogXp)}"
                    + $" R/P/B/G={FormatAmount(result.Red)}/{FormatAmount(result.Purple)}/{FormatAmount(result.Blue)}/{FormatAmount(result.Green)}"
                    + $"\ttheirs read={YesNo(expected.Read)} closed={YesNo(expected.Closed)}"
                    + $" R/P/B/G={FormatAmount(expected.Red)}/{FormatAmount(expected.Purple)}/{FormatAmount(expected.Blue)}/{FormatAmount(expected.Green)}");
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine(
            $"vs tesseract.js: read-flag {readMatch}/{compared}, closed-flag {closedMatch}/{compared}, "
            + $"open-panel read ours {oursReadOpen} theirs {theirsReadOpen}.");
        if (mismatches.Count == 0)
        {
            builder.AppendLine("No mismatches.");
        }
        else
        {
            builder.AppendLine($"Mismatches ({mismatches.Count}):");
            foreach (var line in mismatches)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static Dictionary<string, LampXpBaselineRow> LoadBaselineTsv(string path)
    {
        var map = new Dictionary<string, LampXpBaselineRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("file\t", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 7)
            {
                continue;
            }

            map[parts[0]] = new LampXpBaselineRow(
                ParseBaselineAmount(parts[1]),
                ParseBaselineAmount(parts[2]),
                ParseBaselineAmount(parts[3]),
                ParseBaselineAmount(parts[4]),
                IsYes(parts[5]),
                IsYes(parts[6]));
        }

        return map;
    }

    private static async Task<long?> ReadRowXpAsync(
        string color,
        SoftwareBitmap dialogBitmap,
        OcrEngine engine,
        IReadOnlyList<WordBox> dialogBoxes,
        IReadOnlyDictionary<string, WordBox> dialogRows,
        double? dialogPitch,
        SoftwareBitmap? tableBitmap,
        IReadOnlyList<WordBox> tableBoxes,
        IReadOnlyDictionary<string, WordBox> tableRows,
        double? tablePitch,
        int dialogWidth,
        int dialogHeight,
        string outputDirectory,
        string stem,
        CancellationToken cancellationToken)
    {
        long? tableCrop = null;
        long? tableTokens = null;
        long? dialogTokens = null;
        long? dialogCrop = null;

        if (tableBitmap is not null && tableRows.TryGetValue(color, out var tableAnchor) && tablePitch is > 0)
        {
            var cell = LampGeometry.RowXpCellCrop(
                tableAnchor,
                tablePitch.Value,
                LampGeometry.TableScale,
                tableBitmap.PixelWidth,
                tableBitmap.PixelHeight);
            tableCrop = await ReadCellAsync(
                    tableBitmap,
                    engine,
                    cell,
                    outputDirectory,
                    $"{stem}_row-{color}-table.png",
                    cancellationToken)
                .ConfigureAwait(false);

            var tokens = LampGeometry.RowXpTokens(tableBoxes, tableAnchor, tablePitch.Value, LampGeometry.TableScale);
            if (tokens.Count > 0)
            {
                tableTokens = GameNumber.ParseLine(string.Concat(tokens.Select(w => w.Text)));
            }
        }

        if (dialogRows.TryGetValue(color, out var dialogAnchor))
        {
            var pitch = LampGeometry.EffectivePitch(dialogPitch, dialogAnchor);
            var tokens = LampGeometry.RowXpTokens(dialogBoxes, dialogAnchor, pitch);
            if (tokens.Count > 0)
            {
                dialogTokens = GameNumber.ParseLine(string.Concat(tokens.Select(w => w.Text)));
            }

            var cell = LampGeometry.RowXpCellCrop(dialogAnchor, pitch, 1, dialogWidth, dialogHeight);
            dialogCrop = await ReadCellAsync(
                    dialogBitmap,
                    engine,
                    cell,
                    outputDirectory,
                    $"{stem}_row-{color}-dialog.png",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // Located row + nothing parseable: the x0 cell. WinOCR has no
        // PSM 10 and returns empty for a lone 0 on black (the crop shows
        // the glyph; the engine does not emit it).
        return LampXp.FirstParsed(tableCrop, tableTokens, dialogTokens, dialogCrop)
            ?? (tableRows.ContainsKey(color) || dialogRows.ContainsKey(color) ? 0L : null);
    }

    /// <summary>
    /// Bare <c>0</c> cells are right-aligned on black. WinOCR often returns
    /// empty on the contrast-stretched full cell (no PSM-10 equivalent);
    /// retry without contrast, then on the right half, before giving up.
    /// </summary>
    private static async Task<long?> ReadCellAsync(
        SoftwareBitmap source,
        OcrEngine engine,
        CropRect cell,
        string outputDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (cell.IsEmpty)
        {
            return null;
        }

        var text = await EnhanceAndRecognizeAsync(
                source,
                engine,
                cell,
                LampGeometry.RowXpEnhanceTargetHeight,
                outputDirectory,
                fileName,
                cancellationToken)
            .ConfigureAwait(false);
        var value = GameNumber.ParseLine(text);
        if (value is not null)
        {
            return value;
        }

        var scale = (double)LampGeometry.RowXpEnhanceTargetHeight / Math.Max(cell.Height, 1);
        using (var plain = await ImageEnhance.ScaleCropAsync(source, cell, scale, cancellationToken)
            .ConfigureAwait(false))
        {
            var recognized = await engine.RecognizeAsync(plain).AsTask(cancellationToken).ConfigureAwait(false);
            value = GameNumber.ParseLine(OcrRecognize.JoinRecognizedText(recognized));
            if (value is not null)
            {
                return value;
            }
        }

        if (cell.Width >= 16)
        {
            var right = new CropRect(cell.Left + (cell.Width / 2), cell.Top, cell.Width - (cell.Width / 2), cell.Height);
            if (!right.IsEmpty)
            {
                text = await EnhanceAndRecognizeAsync(
                        source,
                        engine,
                        right,
                        LampGeometry.RowXpEnhanceTargetHeight,
                        outputDirectory,
                        Path.GetFileNameWithoutExtension(fileName) + "-right.png",
                        cancellationToken)
                    .ConfigureAwait(false);
                value = GameNumber.ParseLine(text);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static async Task<(long? Xp, long? Adena)> ReadFarmAmountsAsync(
        DialogCropRecognition dialog,
        IReadOnlyList<WordBox> boxes,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var tokens = FarmFields.ReadTokens(boxes);
        var cropWidth = dialog.CropBitmap!.PixelWidth;
        var cropHeight = dialog.CropBitmap.PixelHeight;

        long? xpFromCrop = null;
        if (tokens.XpTokens.Count > 0)
        {
            var xpRect = FarmFields.XpMicroCrop(tokens.XpTokens, cropWidth, cropHeight);
            if (!xpRect.IsEmpty)
            {
                var text = await EnhanceAndRecognizeAsync(
                        dialog.CropBitmap,
                        dialog.Engine!,
                        xpRect,
                        FarmFields.EnhanceTargetHeight,
                        outputDirectory,
                        Path.GetFileNameWithoutExtension(dialog.SourcePath) + "_xp-for-gate.png",
                        cancellationToken)
                    .ConfigureAwait(false);
                xpFromCrop = GameNumber.ParseLine(text);
            }
        }
        else if (tokens.Unit is not null)
        {
            var xpRect = FarmFields.XpBandCrop(tokens.Unit, tokens.Pitch, cropWidth, cropHeight);
            if (!xpRect.IsEmpty)
            {
                var text = await EnhanceAndRecognizeAsync(
                        dialog.CropBitmap,
                        dialog.Engine!,
                        xpRect,
                        FarmFields.EnhanceTargetHeight,
                        outputDirectory,
                        Path.GetFileNameWithoutExtension(dialog.SourcePath) + "_xp-for-gate.png",
                        cancellationToken)
                    .ConfigureAwait(false);
                xpFromCrop = GameNumber.ParseLine(text);
            }
        }

        var xp = tokens.XpTokens.Count == 0
            ? xpFromCrop ?? tokens.XpFromTokens
            : XpReads.Combine(tokens.XpFromTokens, xpFromCrop);

        long? adenaFromCrop = null;
        if (tokens.AdenaFromTokens is null && tokens.Unit is not null)
        {
            var adenaRect = FarmFields.AdenaFallbackCrop(tokens.Unit, cropWidth, cropHeight);
            if (!adenaRect.IsEmpty)
            {
                var text = await EnhanceAndRecognizeAsync(
                        dialog.CropBitmap,
                        dialog.Engine!,
                        adenaRect,
                        FarmFields.EnhanceTargetHeight,
                        outputDirectory,
                        Path.GetFileNameWithoutExtension(dialog.SourcePath) + "_adena-for-gate.png",
                        cancellationToken)
                    .ConfigureAwait(false);
                adenaFromCrop = GameNumber.ParseLine(text);
            }
        }

        return (xp, tokens.AdenaFromTokens ?? adenaFromCrop);
    }

    private static async Task<string> EnhanceAndRecognizeAsync(
        SoftwareBitmap source,
        OcrEngine engine,
        CropRect crop,
        int targetHeight,
        string outputDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var enhanced = await ImageEnhance.CropAndEnhanceAsync(source, crop, targetHeight, cancellationToken)
            .ConfigureAwait(false);
        Directory.CreateDirectory(outputDirectory);
        var pngPath = Path.Combine(outputDirectory, fileName);
        await OcrRecognize.SavePngAsync(enhanced, pngPath, cancellationToken).ConfigureAwait(false);
        var recognized = await engine.RecognizeAsync(enhanced).AsTask(cancellationToken).ConfigureAwait(false);
        return OcrRecognize.JoinRecognizedText(recognized);
    }

    private static string FormatDump(LampXpResult result, IReadOnlyDictionary<string, long?> parsed)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr lamp table XP");
        builder.AppendLine($"# source: {result.SourcePath}");
        builder.AppendLine($"# image: {result.ImageWidth} x {result.ImageHeight}");
        builder.AppendLine($"# dialog crop: {result.DialogCrop.Left},{result.DialogCrop.Top} {result.DialogCrop.Width}x{result.DialogCrop.Height}");
        builder.AppendLine($"# table crop: {(result.TableCrop.IsEmpty ? "none" : $"{result.TableCrop.Left},{result.TableCrop.Top} {result.TableCrop.Width}x{result.TableCrop.Height}")}");
        builder.AppendLine($"# dialog colors: {string.Join(", ", result.DialogColors)}");
        builder.AppendLine($"# table colors: {string.Join(", ", result.TableColors)}");
        builder.AppendLine($"# dialog xp: {FormatAmount(result.DialogXp)}");
        builder.AppendLine($"# dialog adena: {FormatAmount(result.DialogAdena)}");
        foreach (var color in LampGeometry.Colors)
        {
            builder.AppendLine($"# parsed {color}: {FormatAmount(parsed.GetValueOrDefault(color))}");
        }

        builder.AppendLine($"# lampXpRead: {YesNo(result.LampXpRead)}");
        builder.AppendLine($"# lampPanelClosed: {YesNo(result.LampPanelClosed)}");
        builder.AppendLine($"# exceeds dialog XP: {YesNo(result.ExceedsDialogXp)}");
        builder.AppendLine($"# lamp xp total: {result.LampXpTotal.ToString(CultureInfo.InvariantCulture)}");
        if (result.TablePngPath is not null)
        {
            builder.AppendLine($"# table png: {result.TablePngPath}");
        }

        return builder.ToString();
    }

    private static long? ParseBaselineAmount(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text is "null" or "FAILED")
        {
            return null;
        }

        var compact = text.Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return long.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool IsYes(string text)
        => string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);

    private static string FormatAmount(long? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string Sanitize(string text)
        => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static LampXpResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
    };
}

public sealed record LampXpBaselineRow(
    long? Red,
    long? Purple,
    long? Blue,
    long? Green,
    bool Read,
    bool Closed);
