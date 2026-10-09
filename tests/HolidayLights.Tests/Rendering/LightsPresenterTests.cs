using HolidayLights.Rendering;
using HolidayLights.Tests.Rendering.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Rendering;

public sealed class LightsPresenterTests
{
    private static LightsPresenter Create() =>
        new(SyntheticScene.Resolver, new FakeSpriteProvider(), new FakeFlashEngine(), new FakeMusicEventSource(), new LightsPresenterOptions(), new RecordingLog());

    [Fact]
    public void Constructor_RejectsMissingServices()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LightsPresenter(null!, new FakeSpriteProvider(), new FakeFlashEngine(), new FakeMusicEventSource(), new LightsPresenterOptions(), NullAppLog.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new LightsPresenter(SyntheticScene.Resolver, new FakeSpriteProvider(), new FakeFlashEngine(), new FakeMusicEventSource(), null!, NullAppLog.Instance));
    }

    [Fact]
    public void BeforeStart_TheStatusIsInitialAndCommandsAreHarmless()
    {
        using LightsPresenter presenter = Create();

        Assert.Same(LightsStatus.Initial, presenter.Status);
        Assert.Same(LightsDiagnostics.Empty, presenter.Diagnostics);
        Assert.Equal(FlashSettings.DefaultInterval, presenter.Clock.Interval);

        presenter.Apply(FakeLayout.Scene([SyntheticScene.Display], SyntheticScene.Layout(), LayerMode.BehindIcons));
        presenter.SetPaused(LightsPauseState.None);
        presenter.RetryPreferredLayer();
        presenter.ShowPill(new PillRequest(PillGlyph.LightsOn, "Lights on"));
        presenter.ShowIdentify([SyntheticScene.Display], TimeSpan.FromSeconds(3));
        Assert.True(presenter.ShutdownAsync().IsCompleted);
    }

    [Fact]
    public void Arguments_AreChecked()
    {
        using LightsPresenter presenter = Create();

        Assert.Throws<ArgumentNullException>(() => presenter.Apply(null!));
        Assert.Throws<ArgumentNullException>(() => presenter.SetPaused(null!));
        Assert.Throws<ArgumentNullException>(() => presenter.ShowPill(null!));
        Assert.Throws<ArgumentNullException>(() => presenter.ShowIdentify(null!, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task AfterShutdown_StartDoesNothing()
    {
        using LightsPresenter presenter = Create();
        await presenter.ShutdownAsync(fadeOut: false);
        presenter.Start();

        Assert.Same(LightsStatus.Initial, presenter.Status);
    }

    [Fact]
    public void Options_DefaultToHardwareDrawing() => Assert.False(new LightsPresenterOptions().ForceSoftwareRendering);
}
