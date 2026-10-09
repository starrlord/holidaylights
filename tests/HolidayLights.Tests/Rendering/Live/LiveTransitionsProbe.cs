using System.Diagnostics;
using HolidayLights.Rendering;
using HolidayLights.Tests.Rendering.Fakes;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Live run of the lights' motion paths with the real lights (PRODUCT-SPEC 4.3, 4.4; runs only with
/// <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>): the first-run power-up (the pattern waits for it), a scene update during the wave, a
/// theme transition, a Bulb Drawing move with its fades, Show Lights off and on, Slow Glow with a speed change and Dance to
/// the Music subscribing to music events only while it plays. It checks the state machine (modes, clock, layers, no errors);
/// <see cref="LiveThemeProbe"/> films the wave and <see cref="LivePatternProbe"/> checks the pictures.
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed class LiveTransitionsProbe : IClassFixture<RealLights>
{
    private readonly RealLights lights;
    private readonly ITestOutputHelper output;

    public LiveTransitionsProbe(RealLights lights, ITestOutputHelper output)
    {
        this.lights = lights;
        this.output = output;
    }

    [LiveFact]
    public async Task RunsThePowerUpTransitionsAndContinuousPatterns()
    {
        DesktopCapture.UsePhysicalPixels();
        IReadOnlyList<DisplayInfo> displays = DesktopCapture.Displays();
        ThemeDefinition christmas = lights.Theme("Christmas 1");
        ThemeDefinition halloween = lights.Theme("Halloween");
        var music = new FakeMusicEventSource();
        var log = new RecordingLog();
        int clockChanges = 0;
        using var presenter = new LightsPresenter(lights.Catalog, lights.Sprites, lights.Flash, music, new LightsPresenterOptions(), log);
        presenter.ClockChanged += (_, _) => Interlocked.Increment(ref clockChanges);
        try
        {
            presenter.Start();

            // First-run power-up: lights at once, the pattern starts at step 0 after 2.2 s.
            presenter.Apply(lights.Scene(christmas, displays, LayerMode.BehindIcons) with { Revision = 1 }, SceneTransition.FirstRunPowerUp);
            Assert.True(LiveSupport.WaitFor(() => Shown(presenter, LayerMode.BehindIcons, displays.Count), TimeSpan.FromSeconds(5)));
            Assert.False(presenter.Diagnostics.ClockRunning);

            // A scene update while the wave runs (same pattern) leaves the power-up alone.
            presenter.Apply(lights.Scene(christmas, displays, LayerMode.BehindIcons) with { Revision = 2 }, SceneTransition.Automatic);
            await Task.Delay(400);
            Assert.False(presenter.Diagnostics.ClockRunning, "A scene update started the pattern before the power-up ended.");
            Assert.True(LiveSupport.WaitFor(() => presenter.Diagnostics.ClockRunning, TimeSpan.FromSeconds(4)), "The pattern did not start after the power-up.");
            Assert.True(Volatile.Read(ref clockChanges) >= 1);
            output.WriteLine(
                $"After the power-up: {presenter.Diagnostics.Steps} steps processed, {presenter.Diagnostics.PeriodicLightBulbs} light bulbs on repeating animations, clock changes {clockChanges}.");

            // Theme transition to another arrangement: old lights fade out, short power-up, the new pattern at step 0.
            LightsScene next = lights.Scene(halloween, displays, LayerMode.BehindIcons, r => r with { Pattern = FlashPatternId.Alternating }) with { Revision = 3 };
            presenter.Apply(next, SceneTransition.ThemeTransition);
            await Task.Delay(1_800);
            Assert.True(Shown(presenter, LayerMode.BehindIcons, displays.Count));
            Assert.Equal(next.Layout.Placements.Count, presenter.Diagnostics.Layers.Sum(l => l.Bulbs));
            Assert.True(presenter.Diagnostics.ClockRunning);

            // Bulb Drawing change with its 150 ms fades.
            presenter.Apply(next with { Revision = 4, RequestedLayer = LayerMode.OnTop }, SceneTransition.Automatic);
            Assert.True(LiveSupport.WaitFor(() => Shown(presenter, LayerMode.OnTop, displays.Count), TimeSpan.FromSeconds(2)), "The layers did not move on top.");

            // Show Lights off (300 ms fade) and on again (short power-up).
            presenter.Apply(next with { Revision = 5, RequestedLayer = LayerMode.OnTop, LightsOn = false }, SceneTransition.Automatic);
            Assert.True(LiveSupport.WaitFor(() => presenter.Status.Displays.All(d => d.Effective is null), TimeSpan.FromSeconds(2)), "Lights off did not hide the layers.");
            presenter.Apply(next with { Revision = 6, RequestedLayer = LayerMode.OnTop }, SceneTransition.ShortPowerUp);
            Assert.True(LiveSupport.WaitFor(() => Shown(presenter, LayerMode.OnTop, displays.Count), TimeSpan.FromSeconds(2)), "Lights on did not show the layers.");

            // Slow Glow and a speed change at the next boundary.
            presenter.Apply(lights.Scene(christmas, displays, LayerMode.OnTop, r => r with { Pattern = FlashPatternId.SlowGlow }) with { Revision = 7 }, SceneTransition.Automatic);
            await Task.Delay(700);
            presenter.Apply(lights.Scene(christmas, displays, LayerMode.OnTop, r => r with { Pattern = FlashPatternId.SlowGlow, Interval = 1 }) with { Revision = 8 }, SceneTransition.Automatic);
            Assert.True(LiveSupport.WaitFor(() => presenter.Clock.Interval == 1, TimeSpan.FromSeconds(2)), "The speed change did not reach the clock.");

            // Dance to the Music: subscribes to music events while it plays, and only then.
            presenter.Apply(lights.Scene(lights.Theme("Holiday Party"), displays, LayerMode.OnTop) with { Revision = 9 }, SceneTransition.Automatic);
            Assert.True(LiveSupport.WaitFor(() => music.Subscriptions == 1, TimeSpan.FromSeconds(5)), "Dance did not subscribe to music events.");
            for (int i = 0; i < 20; i++)
            {
                music.Publish(new MusicEvent(MusicEventKind.NoteOn, Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 25), 0, (byte)(60 + (i % 12)), 100));
                await Task.Delay(60);
            }

            presenter.Apply(lights.Scene(christmas, displays, LayerMode.OnTop) with { Revision = 10 }, SceneTransition.Automatic);
            Assert.True(LiveSupport.WaitFor(() => music.Subscriptions == 0, TimeSpan.FromSeconds(5)), "Leaving Dance did not unsubscribe.");
            Assert.True(Shown(presenter, LayerMode.OnTop, displays.Count));
        }
        finally
        {
            await presenter.ShutdownAsync(fadeOut: true).WaitAsync(TimeSpan.FromSeconds(5));
        }

        LiveSupport.WriteLog(log, output);
        Assert.DoesNotContain(log.Entries, e => e.Level == AppLogLevel.Error);
        Assert.Equal(LightsHealth.Stopped, presenter.Status.Health);
        Assert.Equal(0, LiveSupport.CountLayerWindows());
    }

    private static bool Shown(LightsPresenter presenter, LayerMode mode, int displays) =>
        presenter.Status.Displays.Count == displays && presenter.Status.Displays.All(d => d.Effective == mode);
}
