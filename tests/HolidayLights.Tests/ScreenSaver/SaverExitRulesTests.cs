using HolidayLights.App.ScreenSaver.FullScreen;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class SaverExitRulesTests
{
    private const uint KeyDown = 0x0100;
    private const uint SystemKeyDown = 0x0104;
    private const uint LeftButtonDown = 0x0201;
    private const uint RightButtonDown = 0x0204;
    private const uint MiddleButtonDown = 0x0207;
    private const uint XButtonDown = 0x020B;
    private const uint MouseWheel = 0x020A;
    private const uint PointerDown = 0x0246;
    private const uint PowerBroadcast = 0x0218;
    private const uint DisplayChange = 0x007E;
    private const uint ActivateApp = 0x001C;
    private const uint Activate = 0x0006;
    private const uint KillFocus = 0x0008;

    [Theory]
    [InlineData(1.0, 4, 0, false)]
    [InlineData(1.0, 3, 2, true)]
    [InlineData(1.0, -2, -2, false)]
    [InlineData(1.0, 0, -5, true)]
    [InlineData(1.5, 6, 0, false)]
    [InlineData(1.5, 4, 3, true)]
    [InlineData(2.0, 8, 0, false)]
    [InlineData(2.0, 9, 0, true)]
    public void PointerMovement_EndsTheSaverBeyondFourDips(double scale, int dx, int dy, bool ends)
    {
        var start = new PointI(-1000, 500);
        Assert.Equal(ends, SaverExitRules.MovedTooFar(start, new PointI(start.X + dx, start.Y + dy), scale));
    }

    [Theory]
    [InlineData(LeftButtonDown)]
    [InlineData(RightButtonDown)]
    [InlineData(MiddleButtonDown)]
    [InlineData(XButtonDown)]
    [InlineData(MouseWheel)]
    [InlineData(PointerDown)]
    [InlineData(DisplayChange)]
    public void ButtonsWheelTouchAndDisplayChanges_EndTheSaver(uint message) =>
        Assert.True(SaverExitRules.EndsSaver(message, 0, 0));

    [Theory]
    [InlineData(KeyDown)]
    [InlineData(SystemKeyDown)]
    public void NewKeyPresses_EndTheSaver_AutoRepeatOfAHeldKeyDoesNot(uint message)
    {
        Assert.True(SaverExitRules.EndsSaver(message, 0x41, 0x001E0001));
        Assert.False(SaverExitRules.EndsSaver(message, 0x41, 0x401E0001));
    }

    [Theory]
    [InlineData(0x0004, true)]
    [InlineData(0x0007, true)]
    [InlineData(0x0012, true)]
    [InlineData(0x000A, false)]
    [InlineData(0x8013, false)]
    public void SuspendAndResume_EndTheSaver_StatusChangesDoNot(int powerEvent, bool ends) =>
        Assert.Equal(ends, SaverExitRules.EndsSaver(PowerBroadcast, powerEvent, 0));

    [Theory]
    [InlineData(ActivateApp)]
    [InlineData(Activate)]
    [InlineData(KillFocus)]
    public void FocusAndActivationChanges_NeverEndTheSaver(uint message) =>
        Assert.False(SaverExitRules.EndsSaver(message, 0, 0));

    [Fact]
    public void KeyWatcher_IgnoresKeysHeldAtTheStartUntilTheyAreReleased()
    {
        var down = new HashSet<int> { 0x11, 0x0D };
        var watcher = new AsyncKeyWatcher(down.Contains);

        Assert.False(watcher.NewPress());

        down.Remove(0x0D);
        Assert.False(watcher.NewPress());

        down.Add(0x0D);
        Assert.True(watcher.NewPress());
    }

    [Fact]
    public void KeyWatcher_ReportsANewMouseButtonOrKey()
    {
        var down = new HashSet<int>();
        var watcher = new AsyncKeyWatcher(down.Contains);
        down.Add(0x01);
        Assert.True(watcher.NewPress());
        Assert.False(watcher.NewPress());
    }
}
