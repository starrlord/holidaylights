using System.Diagnostics;
using System.Runtime.InteropServices;
using HolidayLights.App.ScreenSaver.Preview;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>
/// The <c>/p</c> preview ends with Windows' preview window however that window goes away (PRODUCT-SPEC 6.2.1), also when
/// its owner ends without destroying it, as when the process of the Screen Saver Settings dialog is killed.
/// </summary>
public sealed partial class PreviewChildWindowTests
{
    private const uint WS_POPUP = 0x80000000;
    private const uint WM_QUIT = 0x0012;
    private const uint PM_REMOVE = 0x0001;

    [Fact]
    public void Run_Ends_WhenTheParentsThreadEndsWithoutDestroyingIt()
    {
        using var kit = new SaverTestKit();
        var settings = new AppSettings
        {
            Current = new ThemeableSettings { Saver = new SaverLook { Picture = SaverPictures.None, Message = "" } },
        };
        nint parent = 0;
        using var parentCreated = new ManualResetEventSlim();
        using var releaseParent = new ManualResetEventSlim();
        var parentThread = new Thread(() =>
        {
            // A hidden window of a predefined class, which needs no window procedure of ours.
            parent = CreateWindowEx(0, "Static", "Holiday Lights test parent", WS_POPUP, 0, 0, 160, 120, 0, 0, 0, 0);
            parentCreated.Set();
            while (!releaseParent.IsSet)
            {
                // Windows' dialog answers the messages its preview child sends it.
                while (PeekMessage(out NativeMessage message, 0, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(in message);
                    DispatchMessage(in message);
                }

                Thread.Sleep(5);
            }

            // The thread ends without DestroyWindow: Windows removes the window, as when its process is killed.
        })
        { IsBackground = true };
        parentThread.Start();
        Assert.True(parentCreated.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(0, parent);

        int exitCode = -1;
        uint previewThreadId = 0;
        Exception? failure = null;
        var previewThread = new Thread(() =>
        {
            previewThreadId = GetCurrentThreadId();
            try
            {
                exitCode = new PreviewChildWindow(kit.Services, settings, SaverTestKit.Display(1920, 1080), parent).Run();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };
        previewThread.SetApartmentState(ApartmentState.STA);
        previewThread.Start();
        try
        {
            var watch = Stopwatch.StartNew();
            while (FindWindowEx(parent, 0, "HolidayLights.ScreenSaver.Preview", null) == 0 && previewThread.IsAlive)
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "The preview window did not appear.");
                Thread.Sleep(20);
            }

            Assert.True(previewThread.IsAlive, $"The preview ended before its parent went away ({failure?.Message}).");
            releaseParent.Set();
            Assert.True(parentThread.Join(TimeSpan.FromSeconds(10)));
            Assert.False(IsWindow(parent));

            Assert.True(previewThread.Join(TimeSpan.FromSeconds(5)), "The preview kept running after its parent window went away.");
            Assert.Null(failure);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            releaseParent.Set();
            if (previewThread.IsAlive && previewThreadId != 0)
            {
                // Never leave a stuck message loop behind.
                PostThreadMessage(previewThreadId, WM_QUIT, 0, 0);
                previewThread.Join(TimeSpan.FromSeconds(5));
            }
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out NativeMessage message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(in NativeMessage message);

    /// <summary><c>MSG</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }
}
