using L2TrackerCompanion.Parsing;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using OcrEngineWord = Windows.Media.Ocr.OcrWord;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Shared <see cref="OcrEngine"/> load/recognize used by the word dump and
/// the dialog crop pass. No field parsing.
/// </summary>
public static class OcrRecognize
{
    public const string PreferredLanguageTag = "en-US";

    public static readonly string[] LampColorNames = ["Red", "Purple", "Blue", "Green"];

    private static readonly object EngineLock = new();
    private static OcrEngine? _sharedEngine;

    /// <summary>
    /// One engine for the whole process. A fresh one used to be built for every
    /// parse, i.e. every 10s poll tick, even though recognition carries no state
    /// between calls. Every pass awaits its recognitions one at a time, so a
    /// single instance serves them all. Never disposed — OcrEngine exposes no
    /// Close/Dispose; if a language pack is installed or removed mid-run, the
    /// app has to be restarted to pick it up.
    /// </summary>
    public static OcrEngine SharedEngine
    {
        get
        {
            lock (EngineLock)
            {
                return _sharedEngine ??= CreateEngine();
            }
        }
    }

    public static OcrEngine CreateEngine()
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

    public static async Task<InMemoryRandomAccessStream> CreateStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync().AsTask().ConfigureAwait(false);
        await writer.FlushAsync().AsTask().ConfigureAwait(false);
        stream.Seek(0);
        return stream;
    }

    public static SoftwareBitmap PrepareForOcr(SoftwareBitmap bitmap)
    {
        var needsConvert = bitmap.BitmapPixelFormat is not (BitmapPixelFormat.Bgra8 or BitmapPixelFormat.Gray8)
            || (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                && bitmap.BitmapAlphaMode == BitmapAlphaMode.Straight);

        return needsConvert
            ? SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
            : SoftwareBitmap.Copy(bitmap);
    }

    public static List<OcrWord> ToWords(OcrResult recognized)
    {
        var words = new List<OcrWord>();
        for (var lineIndex = 0; lineIndex < recognized.Lines.Count; lineIndex++)
        {
            var line = recognized.Lines[lineIndex];
            for (var wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
            {
                words.Add(ToWord(line.Words[wordIndex], lineIndex, wordIndex));
            }
        }

        return words;
    }

    public static OcrWord ToWord(OcrEngineWord word, int lineIndex, int wordIndex)
    {
        var box = word.BoundingRect;
        return new OcrWord
        {
            LineIndex = lineIndex,
            WordIndex = wordIndex,
            Text = word.Text ?? string.Empty,
            X = box.X,
            Y = box.Y,
            Width = box.Width,
            Height = box.Height,
        };
    }

    public static bool ContainsWord(IEnumerable<OcrWord> words, string expected)
        => words.Any(word => string.Equals(word.Text, expected, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> FoundLampColors(IEnumerable<OcrWord> words)
        => LampColorNames.Where(name => ContainsWord(words, name)).ToArray();

    public static IReadOnlyList<WordBox> ToWordBoxes(IEnumerable<OcrWord> words)
        => words.Select(word => new WordBox(word.Text, word.X, word.Y, word.Width, word.Height)).ToList();

    public static string JoinRecognizedText(OcrResult recognized)
        => string.Join('\n', recognized.Lines.Select(line => line.Text ?? string.Empty)).Trim();

    public static async Task<byte[]> EncodePngBytesAsync(SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

        stream.Seek(0);
        var size = (uint)stream.Size;
        using var reader = new DataReader(stream);
        await reader.LoadAsync(size).AsTask(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public static async Task<SoftwareBitmap> DecodePngBytesAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = await CreateStreamAsync(bytes).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return PrepareForOcr(bitmap);
    }

    public static async Task SavePngAsync(SoftwareBitmap bitmap, string path, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = await EncodePngBytesAsync(bitmap, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }
}
