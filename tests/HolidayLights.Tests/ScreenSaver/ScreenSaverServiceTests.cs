using HolidayLights.App.ScreenSaver;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>"Preview Screen Saver" when the saver cannot start (the shown runs are in <see cref="SaverLiveChecks"/>).</summary>
public sealed class ScreenSaverServiceTests
{
    [Fact]
    public void RunPreview_WhenTheSaverCannotStart_ReportsTheSessionStopped_FailsTheTask_AndCanBeTriedAgain()
    {
        var sessions = new RecordingSessions();
        RecordingLog? log = null;
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            log = kit.Log;
            var service = new ScreenSaverService(new SaverAppServices(kit, new BrokenDisplays(), new FakeDirector(), sessions));

            Task first = service.RunPreviewAsync();

            Assert.True(first.IsFaulted);
            Assert.IsType<InvalidOperationException>(first.Exception!.InnerException);
            Assert.False(service.IsPreviewRunning);
            Assert.Equal((1, 1), (sessions.Started, sessions.Stopped));
            Assert.False(sessions.IsSaverRunning);

            Task second = service.RunPreviewAsync();

            Assert.NotSame(first, second);
            Assert.True(second.IsFaulted);
            Assert.IsType<InvalidOperationException>(second.Exception!.InnerException);
            Assert.Equal((2, 2), (sessions.Started, sessions.Stopped));
        }, TimeSpan.FromSeconds(20));

        Assert.Equal(2, log!.Entries.Count(e => e.Level == AppLogLevel.Error && e.Message.Contains("couldn't start", StringComparison.Ordinal)));
    }

    /// <summary>A display service that fails, as while Windows is reconfiguring the displays.</summary>
    private sealed class BrokenDisplays : IDisplayService
    {
        public IReadOnlyList<DisplayInfo> Displays => throw new InvalidOperationException("The displays are being reconfigured.");

        public DisplayInfo Primary => throw new InvalidOperationException("The displays are being reconfigured.");

        public event EventHandler<DisplaysChangedEventArgs>? DisplaysChanged
        {
            add { }
            remove { }
        }

        public DisplayInfo FromPoint(PointI point) => Primary;

        public DisplayInfo FromCursor() => Primary;

        public void Dispose()
        {
        }
    }
}
