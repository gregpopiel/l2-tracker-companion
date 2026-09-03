using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace L2TrackerCompanion.Capture;

/// <summary>
/// Captures a window via Windows.Graphics.Capture (compositor path).
/// Lives in a non-WPF assembly so WinRT interop matches the glasscap/console pattern.
/// </summary>
/// <remarks>
/// Unfocused capture: WGC reads the DWM compositor's backing texture for the target
/// HWND, not the GDI framebuffer of whichever window currently has focus. The
/// companion can stay behind the game (or minimized) while capturing L2.bin — only
/// the game window needs to exist and be visible to the compositor.
/// Verified 2026-09-02 on the developer PC: capture of HWND 0x40B3E succeeded with
/// another app in the foreground (headless CLI, no companion window shown) and with
/// the WPF companion behind Lineage II (manual "Capture once", ~3 MB PNG).
/// Does not work while the game window is minimized (IsIconic) — WGC times out with
/// no compositor frames; same behavior in glasscap. Restore the window (it may stay
/// behind other apps) before capturing.
/// PrintWindow (step 2) failed with ACCESS_DENIED on this client; this path replaced it.
/// </remarks>
public sealed class GraphicsCaptureService
{
    private static readonly Guid DxgiDeviceGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid GraphicsCaptureItemGuid = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid GraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    public CaptureResult CaptureWindow(IntPtr hwnd, string outputPath)
    {
        // WPF runs the UI on an STA thread; WinRT capture needs MTA (same as glasscap).
        CaptureResult result = null;
        Exception threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                _ = RoInitialize(1);
                result = CaptureWindowCore(hwnd, outputPath);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = threadException.Message,
            };
        }

        return result ?? new CaptureResult
        {
            Success = false,
            ErrorMessage = "Capture thread returned no result.",
        };
    }

    private static CaptureResult CaptureWindowCore(IntPtr hwnd, string outputPath)
    {
        try
        {
            var device = CreateWinRtDevice();
            var item = CreateCaptureItem(hwnd);
            var size = item.Size;

            if (size.Width <= 0 || size.Height <= 0)
            {
                return new CaptureResult
                {
                    Success = false,
                    ErrorMessage = "Capture item has zero size.",
                };
            }

            var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);

            using var session = framePool.CreateCaptureSession(item);

            // Windows draws a yellow "capture border" around the captured window by
            // default (Win11 22H2+). IsBorderRequired lets us suppress it, but it's
            // only present on newer builds, so guard with an ApiInformation check.
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
                    "Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            {
                session.IsBorderRequired = false;
            }

            var frameReady = new ManualResetEventSlim(false);
            Direct3D11CaptureFrame capturedFrame = null;

            framePool.FrameArrived += (_, _) =>
            {
                if (capturedFrame != null)
                {
                    return;
                }

                var frame = framePool.TryGetNextFrame();
                if (frame == null)
                {
                    return;
                }

                capturedFrame = frame;
                frameReady.Set();
            };

            session.StartCapture();

            if (!frameReady.Wait(TimeSpan.FromSeconds(8)))
            {
                return new CaptureResult
                {
                    Success = false,
                    ErrorMessage = "Timed out waiting for a capture frame.",
                };
            }

            using (capturedFrame)
            {
                var softwareBitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(capturedFrame.Surface)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                using var converted = SoftwareBitmap.Convert(
                    softwareBitmap,
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);

                SavePng(converted, outputPath);
            }

            framePool.Dispose();

            using var bitmap = new Bitmap(outputPath);
            return new CaptureResult
            {
                Success = true,
                OutputPath = outputPath,
                IsLikelyBlank = IsLikelyBlankFrame(bitmap),
            };
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    internal static bool IsLikelyBlankFrame(Bitmap bitmap)
    {
        const int sampleStride = 37;
        const int darkThreshold = 8;
        var darkSamples = 0;
        var totalSamples = 0;

        for (var y = 0; y < bitmap.Height; y += sampleStride)
        {
            for (var x = 0; x < bitmap.Width; x += sampleStride)
            {
                var pixel = bitmap.GetPixel(x, y);
                totalSamples++;
                if (pixel.R <= darkThreshold && pixel.G <= darkThreshold && pixel.B <= darkThreshold)
                {
                    darkSamples++;
                }
            }
        }

        return totalSamples > 0 && darkSamples == totalSamples;
    }

    private static IDirect3DDevice CreateWinRtDevice()
    {
        const uint bgraSupport = 0x20;
        var hr = D3D11CreateDevice(
            IntPtr.Zero,
            1,
            IntPtr.Zero,
            bgraSupport,
            IntPtr.Zero,
            0,
            7,
            out var device,
            out _,
            out _);

        if (hr != 0)
        {
            hr = D3D11CreateDevice(
                IntPtr.Zero,
                5,
                IntPtr.Zero,
                bgraSupport,
                IntPtr.Zero,
                0,
                7,
                out device,
                out _,
                out _);
        }

        if (hr != 0)
        {
            throw new InvalidOperationException($"D3D11CreateDevice failed (0x{hr:X8}).");
        }

        Guid dxgiGuid = DxgiDeviceGuid;
        Marshal.QueryInterface(device, ref dxgiGuid, out var dxgiDevice);
        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable);
        if (hr != 0)
        {
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed (0x{hr:X8}).");
        }

        return MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
    }

    private static GraphicsCaptureItem CreateCaptureItem(IntPtr hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(className, className.Length, out var hClass);
        try
        {
            var interopIid = GraphicsCaptureItemInteropGuid;
            var hr = RoGetActivationFactory(hClass, ref interopIid, out var factoryPtr);
            if (hr != 0)
            {
                throw new InvalidOperationException($"RoGetActivationFactory failed (0x{hr:X8}).");
            }

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            var itemIid = GraphicsCaptureItemGuid;
            hr = interop.CreateForWindow(hwnd, ref itemIid, out var itemPtr);
            if (hr != 0 || itemPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateForWindow failed (0x{hr:X8}).");
            }

            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            WindowsDeleteString(hClass);
        }
    }

    private static void SavePng(SoftwareBitmap bitmap, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        encoder.SetSoftwareBitmap(bitmap);
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

        stream.Seek(0);
        var length = (uint)stream.Size;
        var reader = new DataReader(stream.GetInputStreamAt(0));
        reader.LoadAsync(length).AsTask().GetAwaiter().GetResult();
        var buffer = new byte[length];
        reader.ReadBytes(buffer);
        File.WriteAllBytes(outputPath, buffer);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }
}
