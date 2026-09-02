using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using L2TrackerCompanion.Capture;

namespace L2TrackerCompanion.Services;

public sealed class WindowCaptureService
{
    private readonly GraphicsCaptureService _graphicsCaptureService = new();
    public const string GameProcessName = "L2.bin";
    public const string ExpectedWindowTitle = "Lineage II";
    public const string DefaultCaptureFileName = "capture.png";
    private const string AppDataFolderName = "L2TrackerCompanion";

    /// <summary>
    /// Fixed Windows profile path — not next to the exe. When the app is launched via
    /// WSL (<c>dotnet run</c> against a \\wsl.localhost\ tree), BaseDirectory is a UNC path
    /// that is awkward to browse and differs from where developers expect output.
    /// </summary>
    public static string GetDefaultCapturePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, DefaultCaptureFileName);
    }

    public GameWindowInfo? TryFindGameWindow()
    {
        var processIds = Process.GetProcessesByName(GameProcessName)
            .Select(process => process.Id)
            .ToHashSet();

        if (processIds.Count == 0)
        {
            return null;
        }

        GameWindowInfo? titledMatch = null;
        GameWindowInfo? fallbackMatch = null;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint windowProcessId);
            if (!processIds.Contains((int)windowProcessId))
            {
                return true;
            }

            var title = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (!TryGetClientSize(hwnd, out var width, out var height))
            {
                return true;
            }

            var candidate = new GameWindowInfo
            {
                Hwnd = hwnd,
                Title = title,
                Width = width,
                Height = height,
                ProcessId = (int)windowProcessId,
            };

            if (string.Equals(title, ExpectedWindowTitle, StringComparison.Ordinal))
            {
                titledMatch = candidate;
                return false;
            }

            fallbackMatch ??= candidate;
            return true;
        }, IntPtr.Zero);

        return titledMatch ?? fallbackMatch;
    }

    public CaptureResult TryCaptureOnce(string? outputPath = null)
    {
        var gameWindow = TryFindGameWindow();
        if (gameWindow is null)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = $"Game not running (no {GameProcessName} process with a visible window).",
            };
        }

        return CaptureWindow(gameWindow, outputPath ?? GetDefaultCapturePath());
    }

    public CaptureResult CaptureWindow(GameWindowInfo window, string outputPath)
    {
        if (window.Width <= 0 || window.Height <= 0)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = "Game window has zero size.",
            };
        }

        if (NativeMethods.IsIconic(window.Hwnd))
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = "Lineage II is minimized — restore the game window before capturing. "
                    + "The game can run behind other windows, but Windows.Graphics.Capture does not "
                    + "receive frames from a minimized (taskbar) window.",
            };
        }

        // PrintWindow fails with ACCESS_DENIED (Win32 error 5) on the L2.bin client.
        // Windows.Graphics.Capture targets the game HWND via the compositor and does
        // not require the companion to be foreground — see GraphicsCaptureService remarks.
        return _graphicsCaptureService.CaptureWindow(window.Hwnd, outputPath);
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryGetClientSize(IntPtr hwnd, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
