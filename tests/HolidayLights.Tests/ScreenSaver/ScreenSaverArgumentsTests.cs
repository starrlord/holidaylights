using HolidayLights.App.ScreenSaver;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class ScreenSaverArgumentsTests
{
    [Theory]
    [InlineData("/s")]
    [InlineData("/S")]
    [InlineData("-s")]
    [InlineData(" /S ")]
    public void RunSwitch_ShowsFullScreen(string argument)
    {
        Assert.True(ScreenSaverArguments.TryParse([argument], launchedAsScreenSaver: true, out ScreenSaverInvocation? invocation));
        Assert.Equal(new ScreenSaverInvocation(ScreenSaverMode.Show, 0), invocation);
    }

    [Theory]
    [InlineData(new[] { "/p", "1234" }, 1234)]
    [InlineData(new[] { "/P", "1234" }, 1234)]
    [InlineData(new[] { "/p:1234" }, 1234)]
    [InlineData(new[] { "-p1234" }, 1234)]
    [InlineData(new[] { "/l", "98765" }, 98765)]
    [InlineData(new[] { "/p" }, 0)]
    [InlineData(new[] { "/p", "not-a-window" }, 0)]
    public void PreviewSwitch_ReadsTheParentWindow(string[] args, long handle)
    {
        Assert.True(ScreenSaverArguments.TryParse(args, launchedAsScreenSaver: true, out ScreenSaverInvocation? invocation));
        Assert.Equal(new ScreenSaverInvocation(ScreenSaverMode.Preview, (nint)handle), invocation);
    }

    [Theory]
    [InlineData(new[] { "/c" }, 0)]
    [InlineData(new[] { "/c:4242" }, 4242)]
    [InlineData(new[] { "/C", "77" }, 77)]
    [InlineData(new[] { "-c" }, 0)]
    public void ConfigureSwitch_ReadsTheOwnerWindow(string[] args, long handle)
    {
        Assert.True(ScreenSaverArguments.TryParse(args, launchedAsScreenSaver: false, out ScreenSaverInvocation? invocation));
        Assert.Equal(new ScreenSaverInvocation(ScreenSaverMode.Configure, (nint)handle), invocation);
    }

    [Fact]
    public void PasswordSwitch_IsRecognisedSoItCanBeIgnored()
    {
        Assert.True(ScreenSaverArguments.TryParse(["/a", "55"], true, out ScreenSaverInvocation? invocation));
        Assert.Equal(new ScreenSaverInvocation(ScreenSaverMode.ChangePassword, 55), invocation);
    }

    [Fact]
    public void NoArguments_ConfigureOnlyWhenRunningAsTheScr()
    {
        Assert.True(ScreenSaverArguments.TryParse([], launchedAsScreenSaver: true, out ScreenSaverInvocation? invocation));
        Assert.Equal(new ScreenSaverInvocation(ScreenSaverMode.Configure, 0), invocation);

        Assert.False(ScreenSaverArguments.TryParse([], launchedAsScreenSaver: false, out ScreenSaverInvocation? none));
        Assert.Null(none);
    }

    [Theory]
    [InlineData("--settings")]
    [InlineData("--reset")]
    [InlineData("--data-root")]
    [InlineData("/sound")]
    [InlineData("/px")]
    [InlineData("/x")]
    [InlineData("settings")]
    [InlineData("s")]
    [InlineData("/")]
    public void OtherCommandLines_AreNotScreenSaverSwitches(string argument)
    {
        Assert.False(ScreenSaverArguments.TryParse([argument, "123"], true, out ScreenSaverInvocation? invocation));
        Assert.Null(invocation);
    }
}
