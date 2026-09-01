namespace L2TrackerCompanion.Services;

public sealed class GameWindowInfo
{
    public required IntPtr Hwnd { get; init; }

    public required string Title { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int ProcessId { get; init; }
}
