using System.Diagnostics;

namespace HolidayLights.App.Shell;

/// <summary>
/// The step clock of sessions without a presenter (settings-only, screen saver): previews follow it exactly as they follow
/// the presenter's clock (PRODUCT-SPEC 5.5.2). A new pattern restarts it at step 0; a speed change takes effect at the
/// next step boundary without resetting the step (5.5.1).
/// </summary>
public sealed class FreeRunningClock : IStepClockSource
{
    private StepClock clock;
    private FlashPatternId pattern;

    /// <summary>Starts the clock for a scene.</summary>
    /// <param name="scene">The first scene.</param>
    public FreeRunningClock(LightsScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        clock = StepClock.Start(Stopwatch.GetTimestamp(), scene.Interval);
        pattern = scene.Flash.Pattern;
    }

    /// <inheritdoc />
    public event EventHandler? ClockChanged;

    /// <inheritdoc />
    public StepClock Clock => Volatile.Read(ref clock);

    /// <summary>Follows a new scene: restart on a new pattern, change speed on a new interval.</summary>
    /// <param name="scene">The scene.</param>
    public void Follow(LightsScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        long now = Stopwatch.GetTimestamp();
        StepClock current = Clock;
        StepClock next = scene.Flash.Pattern != pattern
            ? StepClock.Start(now, scene.Interval)
            : scene.Interval != current.Interval ? current.WithInterval(scene.Interval, now) : current;
        pattern = scene.Flash.Pattern;
        if (!ReferenceEquals(next, current))
        {
            Volatile.Write(ref clock, next);
            ClockChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
