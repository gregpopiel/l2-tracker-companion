using System.Windows;
using System.Windows.Threading;
using L2TrackerCompanion.Services;

namespace L2TrackerCompanion;

public partial class MainWindow : Window
{
    private readonly WindowCaptureService _windowCaptureService = new();
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += (_, _) => RefreshGameWindowStatus();
        _refreshTimer.Start();

        RefreshGameWindowStatus();
    }

    private void RefreshGameWindowStatus()
    {
        var gameWindow = _windowCaptureService.TryFindGameWindow();
        GameWindowStatusLabel.Text = gameWindow is null
            ? $"Game not running (no {WindowCaptureService.GameProcessName} process with a visible window)."
            : $"Found HWND 0x{gameWindow.Hwnd.ToInt64():X}\n"
              + $"Title: {gameWindow.Title}\n"
              + $"Size: {gameWindow.Width} x {gameWindow.Height}\n"
              + $"PID: {gameWindow.ProcessId}";
    }
}
