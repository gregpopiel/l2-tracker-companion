using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using L2TrackerCompanion.Parsing;
using DrawingBitmap = System.Drawing.Bitmap;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Browser <c>cropAndEnhance</c>: crop + scale to <c>targetHeight</c> in one
/// resample, then grayscale and <c>clamp(gray * 1.8 - 60, 0, 255)</c>. WinRT
/// has no filters; GDI+ HighQualityBicubic is the closest built-in match to
/// canvas <c>imageSmoothingQuality = 'high'</c>.
/// </summary>
public static class ImageEnhance
{
    public static async Task<SoftwareBitmap> CropAndEnhanceAsync(
        SoftwareBitmap source,
        CropRect crop,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        using var sourceBmp = await ToBitmapAsync(source, cancellationToken).ConfigureAwait(false);
        using var enhanced = CropAndEnhance(sourceBmp, crop, targetHeight);
        var png = EncodeBitmapPng(enhanced);
        return await OcrRecognize.DecodePngBytesAsync(png, cancellationToken).ConfigureAwait(false);
    }

    public static DrawingBitmap CropAndEnhance(DrawingBitmap source, CropRect crop, int targetHeight)
    {
        var x = Math.Max(0, crop.Left);
        var y = Math.Max(0, crop.Top);
        var w = Math.Min(crop.Width, source.Width - x);
        var h = Math.Min(crop.Height, source.Height - y);
        if (w <= 0 || h <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), "Crop is empty or outside the source.");
        }

        var scale = (double)targetHeight / h;
        var outW = Math.Max(1, (int)Math.Round(w * scale, MidpointRounding.AwayFromZero));
        var upscaled = new DrawingBitmap(outW, targetHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(upscaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, outW, targetHeight), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
        }

        ContrastStretch(upscaled);
        return upscaled;
    }

    public static void SavePng(DrawingBitmap bitmap, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    static async Task<DrawingBitmap> ToBitmapAsync(SoftwareBitmap softwareBitmap, CancellationToken cancellationToken)
    {
        var png = await OcrRecognize.EncodePngBytesAsync(softwareBitmap, cancellationToken).ConfigureAwait(false);
        using var ms = new MemoryStream(png);
        using var loaded = new DrawingBitmap(ms);
        var clone = new DrawingBitmap(loaded.Width, loaded.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(clone);
        g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
        return clone;
    }

    static byte[] EncodeBitmapPng(DrawingBitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    static void ContrastStretch(DrawingBitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            var n = Math.Abs(data.Stride) * data.Height;
            var bytes = new byte[n];
            Marshal.Copy(data.Scan0, bytes, 0, n);
            var stride = Math.Abs(data.Stride);
            for (var y = 0; y < bmp.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < bmp.Width; x++)
                {
                    var i = row + (x * 3);
                    // Format24bppRgb is B, G, R. Match canvas 0.299/0.587/0.114.
                    var gray = (0.299 * bytes[i + 2]) + (0.587 * bytes[i + 1]) + (0.114 * bytes[i]);
                    var v = (byte)Math.Clamp((int)Math.Round(gray * 1.8 - 60, MidpointRounding.AwayFromZero), 0, 255);
                    bytes[i] = bytes[i + 1] = bytes[i + 2] = v;
                }
            }

            Marshal.Copy(bytes, 0, data.Scan0, n);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
