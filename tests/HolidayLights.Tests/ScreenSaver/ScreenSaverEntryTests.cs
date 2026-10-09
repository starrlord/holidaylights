using HolidayLights.App.ScreenSaver;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>The program modes where nothing is shown (the shown ones are in <see cref="SaverLiveChecks"/>).</summary>
public sealed class ScreenSaverEntryTests
{
    [Fact]
    public void RunFullScreen_WithoutDisplays_EndsAtOnce_AndNeverStartsTheMusic()
    {
        var music = new FakeDirector();
        var sessions = new RecordingSessions();
        int exitCode = -1;
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            exitCode = ScreenSaverEntry.RunFullScreen(new SaverAppServices(kit, new FakeDisplays(), music, sessions));
        }, TimeSpan.FromSeconds(10));

        Assert.Equal(0, exitCode);
        Assert.Empty(music.Policies);
        Assert.Null(music.StoppedWith);
        Assert.Equal((0, 0), (sessions.Started, sessions.Stopped));
    }

    [Fact]
    public void RunPreview_WhenWindowsPreviewWindowIsGone_ReturnsOne_WithoutAWindow()
    {
        int exitCode = -1;
        RecordingLog? log = null;
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            log = kit.Log;
            exitCode = ScreenSaverEntry.RunPreview(new SaverAppServices(kit, new FakeDisplays(), new FakeDirector(), new RecordingSessions()), parentWindow: 0);
        }, TimeSpan.FromSeconds(10));

        Assert.Equal(1, exitCode);
        Assert.Contains(log!.Entries, e => e.Message.Contains("preview window is gone", StringComparison.Ordinal));
    }
}
