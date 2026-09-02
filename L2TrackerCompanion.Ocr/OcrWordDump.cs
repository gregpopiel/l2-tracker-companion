using System.Globalization;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Step 5 smoke: run <see cref="OcrEngine"/> on a PNG and dump every word plus its
/// bounding box. No field parsing — later steps consume this dump (or a live
/// RecognizeAsync call) to locate "Play Report", the "adena" unit, and lamp colours.
/// Boxes are in source-image pixels, origin top-left.
/// </summary>
/// <remarks>
/// Measured 2026-09-02 on the POC set with Windows.Media.Ocr (en-GB; en-US pack was
/// not installed). Dialog crops (~540–643px): <c>adena</c>, <c>play</c>/<c>Play</c>,
/// <c>Characters</c>, and Red/Purple/Green dump with stable boxes; row-name Y values
/// step by ~38px (usable pitch). <c>Report</c> is consistently <c>Rewrt</c>/<c>Rewt</c>/<c>Recort</c>
/// (~33×9–10px glyphs). Blue is often <c>gue</c>/<c>Nue</c>. A full-desktop 1904×996
/// pass finds <c>adena</c> and <c>Characters</c> but no lamp colour names — same reason
/// the browser locate pass crops before reading the table. Locate in later steps should
/// keep the "Characters" fallback; do not assume an exact "Report" token.
/// </remarks>
public static class OcrWordDump
{
    public const string AppDataFolderName = "L2TrackerCompanion";
    public const string DefaultDumpFileName = "ocr-words.txt";
    public const string DefaultBatchDumpFolderName = "ocr-poc-dumps";
    public const string PreferredLanguageTag = OcrRecognize.PreferredLanguageTag;

    public static string GetDefaultDumpPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, DefaultDumpFileName);
    }

    public static string GetDefaultBatchDumpDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName,
            DefaultBatchDumpFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static IReadOnlyList<string> ListPngsInDirectory(string imageDirectory)
    {
        if (!Directory.Exists(imageDirectory))
        {
            return [];
        }

        // Top-level only — skip images/processed/ (tesseract.js intermediates).
        return Directory.GetFiles(imageDirectory, "*.png")
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatBatchSummary(IReadOnlyList<OcrDumpResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr batch dump — no parsing");
        builder.AppendLine($"# files: {results.Count}");
        builder.AppendLine($"# succeeded: {results.Count(r => r.Success)}");
        builder.AppendLine($"# failed: {results.Count(r => !r.Success)}");

        foreach (var kind in new[] { "dialog", "framed", "desktop" })
        {
            var group = results.Where(r => r.Success && r.FrameKind == kind).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            builder.AppendLine(
                $"# {kind} n={group.Count} "
                + $"locate={group.Count(r => r.FoundLocateAnchor)} "
                + $"adena={group.Count(r => r.FoundAdena)} "
                + $"lamps={group.Count(r => r.FoundLampColors.Count > 0)} "
                + $"Play={group.Count(r => r.FoundPlay)} "
                + $"Report={group.Count(r => r.FoundReport)} "
                + $"Characters={group.Count(r => r.FoundCharacters)}");
        }

        builder.AppendLine("file\tkind\twidth\theight\twords\tPlay\tReport\tCharacters\tadena\tlamps\tlocate\tsmoke");
        foreach (var result in results)
        {
            var name = result.SourcePath is null
                ? ""
                : Path.GetFileName(result.SourcePath);
            var lamps = result.FoundLampColors.Count == 0
                ? "(none)"
                : string.Join(",", result.FoundLampColors);
            builder.Append(SanitizeText(name));
            builder.Append('\t');
            builder.Append(result.Success ? result.FrameKind : "fail");
            builder.Append('\t');
            builder.Append(result.ImageWidth.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(result.ImageHeight.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(result.Words.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(YesNo(result.FoundPlay));
            builder.Append('\t');
            builder.Append(YesNo(result.FoundReport));
            builder.Append('\t');
            builder.Append(YesNo(result.FoundCharacters));
            builder.Append('\t');
            builder.Append(YesNo(result.FoundAdena));
            builder.Append('\t');
            builder.Append(lamps);
            builder.Append('\t');
            builder.Append(YesNo(result.FoundLocateAnchor));
            builder.Append('\t');
            builder.AppendLine(result.Success
                ? (result.SmokePassed ? "PASS" : "FAIL")
                : result.ErrorMessage ?? "error");
        }

        return builder.ToString();
    }

    public static string FormatBatchStatus(IReadOnlyList<OcrDumpResult> results)
    {
        var ok = results.Count(r => r.Success);
        var lines = new List<string> { $"Batch: {ok}/{results.Count} dumps written." };
        foreach (var kind in new[] { "dialog", "framed", "desktop" })
        {
            var group = results.Where(r => r.Success && r.FrameKind == kind).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            lines.Add(
                $"{kind}: {group.Count}  locate {group.Count(r => r.FoundLocateAnchor)}/{group.Count}  "
                + $"adena {group.Count(r => r.FoundAdena)}/{group.Count}  "
                + $"lamps {group.Count(r => r.FoundLampColors.Count > 0)}/{group.Count}");
        }

        return string.Join("\n", lines);
    }

    public static Task<OcrDumpResult> DumpFileAsync(string imagePath, string? outputPath = null)
        => DumpFileAsync(imagePath, outputPath ?? GetDefaultDumpPath(), CancellationToken.None);

    public static async Task<OcrDumpResult> DumpFileAsync(
        string imagePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return Fail($"PNG not found: {imagePath}");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
            using var stream = await OcrRecognize.CreateStreamAsync(bytes).ConfigureAwait(false);

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
            using var prepared = OcrRecognize.PrepareForOcr(bitmap);

            var engine = OcrRecognize.CreateEngine();
            var recognized = await engine.RecognizeAsync(prepared).AsTask(cancellationToken).ConfigureAwait(false);
            var words = OcrRecognize.ToWords(recognized);

            var result = new OcrDumpResult
            {
                Success = true,
                SourcePath = Path.GetFullPath(imagePath),
                OutputPath = Path.GetFullPath(outputPath),
                ImageWidth = decoder.PixelWidth,
                ImageHeight = decoder.PixelHeight,
                Language = engine.RecognizerLanguage.LanguageTag,
                LineCount = recognized.Lines.Count,
                Words = words,
                FoundPlay = OcrRecognize.ContainsWord(words, "Play") || OcrRecognize.ContainsWord(words, "PlayReport"),
                FoundReport = OcrRecognize.ContainsWord(words, "Report") || OcrRecognize.ContainsWord(words, "PlayReport"),
                FoundCharacters = OcrRecognize.ContainsWord(words, "Characters"),
                FoundAdena = OcrRecognize.ContainsWord(words, "adena"),
                FoundLampColors = OcrRecognize.FoundLampColors(words),
            };

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outputPath, FormatDump(result), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static string FormatStatus(OcrDumpResult result)
    {
        if (!result.Success)
        {
            return $"OCR dump failed: {result.ErrorMessage}";
        }

        var lamps = result.FoundLampColors.Count == 0
            ? "(none)"
            : string.Join(", ", result.FoundLampColors);

        var smoke = result.SmokePassed ? "PASS" : "FAIL";
        return $"Wrote {result.OutputPath}\n"
            + $"{result.Words.Count} words in {result.LineCount} lines "
            + $"({result.ImageWidth}x{result.ImageHeight}, {result.Language})\n"
            + $"smoke {smoke}: Play={YesNo(result.FoundPlay)} "
            + $"Report={YesNo(result.FoundReport)} "
            + $"Characters={YesNo(result.FoundCharacters)} "
            + $"adena={YesNo(result.FoundAdena)} "
            + $"lamps={lamps}";
    }

    private static string FormatDump(OcrDumpResult result)
    {
        var lamps = result.FoundLampColors.Count == 0
            ? "(none)"
            : string.Join(", ", result.FoundLampColors);

        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr word dump — no parsing");
        builder.AppendLine($"# source: {result.SourcePath}");
        builder.AppendLine($"# image: {result.ImageWidth} x {result.ImageHeight}");
        builder.AppendLine($"# language: {result.Language}");
        builder.AppendLine($"# lines: {result.LineCount}");
        builder.AppendLine($"# words: {result.Words.Count}");
        builder.AppendLine($"# smoke Play: {YesNo(result.FoundPlay)}");
        builder.AppendLine($"# smoke Report: {YesNo(result.FoundReport)}");
        builder.AppendLine($"# smoke Characters: {YesNo(result.FoundCharacters)}");
        builder.AppendLine($"# smoke adena: {YesNo(result.FoundAdena)}");
        builder.AppendLine($"# smoke lamp colors: {lamps}");
        builder.AppendLine($"# smoke: {(result.SmokePassed ? "PASS" : "FAIL")}");
        builder.AppendLine("line\tword\tx\ty\twidth\theight\ttext");

        foreach (var word in result.Words)
        {
            builder.Append(word.LineIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(word.WordIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(FormatNumber(word.X));
            builder.Append('\t');
            builder.Append(FormatNumber(word.Y));
            builder.Append('\t');
            builder.Append(FormatNumber(word.Width));
            builder.Append('\t');
            builder.Append(FormatNumber(word.Height));
            builder.Append('\t');
            builder.AppendLine(SanitizeText(word.Text));
        }

        return builder.ToString();
    }

    private static string FormatNumber(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string SanitizeText(string text)
        => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static OcrDumpResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
    };
}
