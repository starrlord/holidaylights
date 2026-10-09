using HolidayLights.Rendering.Shell;

namespace HolidayLights.Tests.Rendering;

public sealed class DesktopHostLocatorTests
{
    private const uint Explorer = 4242;

    [Fact]
    public void RaisedDesktop_UsesProgmanAsTheLayerParent()
    {
        var shell = new FakeShell()
            .Window(1, "Progman", parent: 0, noRedirection: true)
            .Window(2, "SHELLDLL_DefView", parent: 1)
            .Window(3, "WorkerW", parent: 1);

        DesktopHosts hosts = DesktopHostLocator.Locate(shell);

        Assert.Equal(DesktopLayout.Raised, hosts.Layout);
        Assert.Equal((nint)1, hosts.Progman);
        Assert.Equal((nint)2, hosts.DefView);
        Assert.Equal((nint)3, hosts.WallpaperWorker);
        Assert.Equal((nint)1, hosts.LayerParent);
    }

    [Fact]
    public void RaisedDesktopWithoutWallpaperWorker_IsNotSplitYet()
    {
        var shell = new FakeShell()
            .Window(1, "Progman", parent: 0, noRedirection: true)
            .Window(2, "SHELLDLL_DefView", parent: 1);

        DesktopHosts hosts = DesktopHostLocator.Locate(shell);

        Assert.Equal(DesktopLayout.NotSplit, hosts.Layout);
        Assert.Equal((nint)0, hosts.LayerParent);
    }

    [Fact]
    public void ClassicDesktop_UsesTheWallpaperWorkerAfterTheIconWorker()
    {
        var shell = new FakeShell()
            .Window(5, "WorkerW", parent: 0)
            .Window(6, "SHELLDLL_DefView", parent: 5)
            .Window(7, "WorkerW", parent: 0)
            .Window(1, "Progman", parent: 0);

        DesktopHosts hosts = DesktopHostLocator.Locate(shell);

        Assert.Equal(DesktopLayout.Classic, hosts.Layout);
        Assert.Equal((nint)6, hosts.DefView);
        Assert.Equal((nint)7, hosts.WallpaperWorker);
        Assert.Equal((nint)7, hosts.LayerParent);
    }

    [Fact]
    public void ClassicDesktop_IgnoresAWorkerOfAnotherProcess()
    {
        var shell = new FakeShell()
            .Window(5, "WorkerW", parent: 0)
            .Window(6, "SHELLDLL_DefView", parent: 5)
            .Window(7, "WorkerW", parent: 0, process: 99)
            .Window(1, "Progman", parent: 0);

        DesktopHosts hosts = DesktopHostLocator.Locate(shell);

        Assert.Equal(DesktopLayout.NotSplit, hosts.Layout);
        Assert.Equal((nint)0, hosts.LayerParent);
    }

    [Fact]
    public void ClassicDesktopBeforeTheSplit_FindsTheIconsInProgman()
    {
        var shell = new FakeShell()
            .Window(1, "Progman", parent: 0)
            .Window(2, "SHELLDLL_DefView", parent: 1);

        DesktopHosts hosts = DesktopHostLocator.Locate(shell);

        Assert.Equal(DesktopLayout.NotSplit, hosts.Layout);
        Assert.Equal((nint)1, hosts.Progman);
    }

    [Fact]
    public void WithoutProgman_ThereIsNoShell()
    {
        DesktopHosts hosts = DesktopHostLocator.Locate(new FakeShell().Window(9, "Shell_TrayWnd", parent: 0));

        Assert.Equal(DesktopHosts.None, hosts);
        Assert.Equal((nint)0, hosts.LayerParent);
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void ThisDesktop_IsFoundWithoutChangingIt()
    {
        // Read-only probe of the real shell: the locator only queries windows and never sends 0x052C.
        DesktopHosts hosts = DesktopHostLocator.Locate(Win32ShellWindows.Instance);

        Assert.NotEqual(DesktopLayout.NoShell, hosts.Layout);
        Assert.NotEqual((nint)0, hosts.Progman);
        if (hosts.Layout == DesktopLayout.Raised)
        {
            Assert.Equal(hosts.Progman, hosts.LayerParent);
            Assert.NotEqual((nint)0, hosts.DefView);
        }
    }

    /// <summary>A window tree in z-order (top-level windows in insertion order).</summary>
    private sealed class FakeShell : IShellWindows
    {
        private readonly List<(nint Hwnd, string Class, nint Parent, bool NoRedirection, uint Process)> windows = [];

        public FakeShell Window(nint hwnd, string className, nint parent, bool noRedirection = false, uint process = Explorer)
        {
            windows.Add((hwnd, className, parent, noRedirection, process));
            return this;
        }

        public nint Find(nint parent, nint after, string className)
        {
            bool started = after == 0;
            foreach ((nint hwnd, string cls, nint windowParent, _, _) in windows)
            {
                if (windowParent != parent)
                {
                    continue;
                }

                if (!started)
                {
                    started = hwnd == after;
                    continue;
                }

                if (cls == className)
                {
                    return hwnd;
                }
            }

            return 0;
        }

        public bool HasNoRedirectionBitmap(nint hwnd) => windows.Single(w => w.Hwnd == hwnd).NoRedirection;

        public uint ProcessIdOf(nint hwnd) => windows.Single(w => w.Hwnd == hwnd).Process;

        public IReadOnlyList<nint> TopLevelWindows() => [.. windows.Where(w => w.Parent == 0).Select(w => w.Hwnd)];
    }
}
