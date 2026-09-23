using System.Windows.Input;

namespace L2TrackerCompanion;

/// <summary>
/// Commands the theme template can bind without a code-behind FindName.
/// The tab-bar website button lives in TabControl's ControlTemplate, so a
/// RoutedCommand on the Window is what survives template recreation.
/// </summary>
public static class DesktopCommands
{
    public static readonly RoutedUICommand OpenWebsite = new(
        "Open l2tracker.cc",
        nameof(OpenWebsite),
        typeof(DesktopCommands));
}
