using System.Globalization;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

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
    public const string PreferredLanguageTag = "en-US";

    private static readonly string[] LampColorNames = ["Red", "Purple", "Blue", "Green"];

    public static string GetDefaultDumpPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, DefaultDumpFileName);
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
            using var stream = await CreateStreamAsync(bytes).ConfigureAwait(false);

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
            using var prepared = PrepareForOcr(bitmap);

            var engine = CreateEngine();
            var recognized = await engine.RecognizeAsync(prepared).AsTask(cancellationToken).ConfigureAwait(false);

            var words = new List<OcrWord>();
            for (var lineIndex = 0; lineIndex < recognized.Lines.Count; lineIndex++)
            {
                var line = recognized.Lines[lineIndex];
                for (var wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
                {
                    var word = line.Words[wordIndex];
                    var box = word.BoundingRect;
                    words.Add(new OcrWord
                    {
                        LineIndex = lineIndex,
                        WordIndex = wordIndex,
                        Text = word.Text ?? string.Empty,
                        X = box.X,
                        Y = box.Y,
                        Width = box.Width,
                        Height = box.Height,
                    });
                }
            }

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
                FoundPlay = ContainsWord(words, "Play") || ContainsWord(words, "PlayReport"),
                FoundReport = ContainsWord(words, "Report") || ContainsWord(words, "PlayReport"),
                FoundCharacters = ContainsWord(words, "Characters"),
                FoundAdena = ContainsWord(words, "adena"),
                FoundLampColors = LampColorNames.Where(name => ContainsWord(words, name)).ToArray(),
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

    private static OcrEngine CreateEngine()
    {
        var preferred = new Language(PreferredLanguageTag);
        var engine = OcrEngine.TryCreateFromLanguage(preferred)
            ?? OcrEngine.TryCreateFromUserProfileLanguages();

        if (engine is null)
        {
            var available = string.Join(", ",
                OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag));
            throw new InvalidOperationException(
                "Windows.Media.Ocr has no recognizer language. Install the English OCR pack "
                + "(Settings → Time & language → Language & region → English → Language options → "
                + "Optical character recognition)."
                + (string.IsNullOrEmpty(available) ? string.Empty : $" Available: {available}"));
        }

        return engine;
    }

    private static SoftwareBitmap PrepareForOcr(SoftwareBitmap bitmap)
    {
        var needsConvert = bitmap.BitmapPixelFormat is not (BitmapPixelFormat.Bgra8 or BitmapPixelFormat.Gray8)
            || (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                && bitmap.BitmapAlphaMode == BitmapAlphaMode.Straight);

        return needsConvert
            ? SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
            : SoftwareBitmap.Copy(bitmap);
    }

    private static async Task<InMemoryRandomAccessStream> CreateStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync().AsTask().ConfigureAwait(false);
        await writer.FlushAsync().AsTask().ConfigureAwait(false);
        stream.Seek(0);
        return stream;
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

    private static bool ContainsWord(IEnumerable<OcrWord> words, string expected)
        => words.Any(word => string.Equals(word.Text, expected, StringComparison.OrdinalIgnoreCase));

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
