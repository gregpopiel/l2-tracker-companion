using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace L2TrackerCompanion;

/// <summary>
/// The title bar's tracking-status dot from the Session redesign, rendered
/// as a <see cref="Window.Icon"/> swap rather than custom window chrome —
/// no icon asset files needed beyond the app's own:
/// <see cref="RenderTargetBitmap"/> is itself a <see cref="BitmapSource"/>,
/// which <c>Window.Icon</c> accepts directly.
/// </summary>
/// <remarks>
/// A badge on the app icon, not a replacement for it: <c>Window.Icon</c> also
/// drives the taskbar button and Alt+Tab, so a bare dot there would cost the
/// app its identity everywhere Windows shows it — permanently, since Idle is
/// the normal state whenever tracking is off.
///
/// Takes a colour rather than owning one: the palette's own Confirm Green /
/// Alarm Red / Idle Gray are resolved from the theme by the caller, so a
/// palette edit reaches this surface too instead of leaving it spelled a
/// second way in hex here.
/// </remarks>
internal static class StatusDotIcon
{
    private const int Size = 32;

    /// <summary>The app mark the status dot is badged onto.</summary>
    public static ImageSource? TryLoadAppIcon()
    {
        try
        {
            // Decoded at the size it is composited at, so the .ico's own 32px
            // frame is the one picked rather than a downscaled larger one.
            var icon = new BitmapImage();
            icon.BeginInit();
            icon.UriSource = new Uri(
                "pack://application:,,,/L2TrackerCompanion;component/Assets/app.ico",
                UriKind.Absolute);
            icon.DecodePixelWidth = Size;
            icon.DecodePixelHeight = Size;
            icon.CacheOption = BitmapCacheOption.OnLoad;
            icon.EndInit();
            icon.Freeze();
            return icon;
        }
        catch (Exception ex)
        {
            // A missing/unreadable resource must not take the window down over
            // a decoration — Render falls back to the dot on its own.
            System.Diagnostics.Trace.WriteLine(ex);
            return null;
        }
    }

    public static ImageSource Render(ImageSource? appIcon, Color color)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (appIcon is not null)
            {
                context.DrawImage(appIcon, new Rect(0, 0, Size, Size));
            }

            // Bottom-right corner at a third of the icon, the shape Windows'
            // own overlay badges use — still legible at the 16px the title bar
            // draws, without hiding the mark underneath.
            var radius = appIcon is null ? Size / 2.0 - 2 : Size / 3.0;
            var centre = appIcon is null
                ? new Point(Size / 2.0, Size / 2.0)
                : new Point(Size - radius - 1, Size - radius - 1);

            // A dark ring keeps the dot readable against whatever the mark
            // puts behind it.
            Pen? ring = appIcon is null
                ? null
                : new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x0D, 0x11, 0x17)), 2);
            context.DrawEllipse(new SolidColorBrush(color), ring, centre, radius, radius);
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
