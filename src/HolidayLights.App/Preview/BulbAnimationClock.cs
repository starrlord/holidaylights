using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace HolidayLights.App.Preview;

/// <summary>Something that animates on the shared step ticks (a tile run, a chip, a corner box, a theme card strip).</summary>
public interface IStepAnimated
{
    /// <summary>The element on screen (its window decides whether it animates).</summary>
    FrameworkElement Element { get; }

    /// <summary>Shows a step (called on the UI thread).</summary>
    /// <param name="step">The step to show.</param>
    /// <param name="clock">The desktop's step clock.</param>
    void ShowStep(long step, StepClock clock);
}

/// <summary>
/// The one shared UI timer of the bulb previews (PRODUCT-SPEC 3.0.3): it ticks with the flash step period, never faster
/// than every 150 ms, advances only realized items that are visible inside their list's viewport, and stops while their
/// windows are inactive or minimized and while the lights rest because nobody can see the screen (locked, display off,
/// user away). Items show the desktop's step, so every preview blinks in unison with the lights.
/// </summary>
public sealed class BulbAnimationClock
{
    /// <summary>The fastest tick (PRODUCT-SPEC 3.0.3).</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(150);

    private static readonly ConditionalWeakTable<IAppServices, BulbAnimationClock> Clocks = new();

    private readonly IAppServices services;
    private readonly DispatcherTimer timer;
    private readonly List<IStepAnimated> items = [];
    private readonly HashSet<Window> windows = [];
    private long lastStep = long.MinValue;

    private BulbAnimationClock(IAppServices services)
    {
        this.services = services;
        timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += (_, _) => Tick();
        services.Lights.SceneChanged += (_, _) => Reschedule();
        services.Lights.StatusChanged += (_, _) => Reschedule();
        services.Lights.Clock.ClockChanged += (_, _) => timer.Dispatcher.BeginInvoke(Reschedule);
    }

    /// <summary>True while the timer is armed (for tests).</summary>
    internal bool IsTicking => timer.IsEnabled;

    /// <summary>The clock of a service set (one per application, created on its UI thread).</summary>
    /// <param name="services">The services.</param>
    /// <returns>The clock.</returns>
    public static BulbAnimationClock For(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Clocks.GetValue(services, s => new BulbAnimationClock(s));
    }

    /// <summary>True when the desktop pattern changes over time (not "Don't Flash", not "Stop Flashing").</summary>
    public bool IsRunningPattern => services.Lights.Scene is { } scene && HolidayLights.Core.Flash.FlashClock.RunsClock(scene.Flash);

    /// <summary>Starts animating an item and shows the current step at once.</summary>
    /// <param name="item">The item.</param>
    public void Register(IStepAnimated item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!items.Contains(item))
        {
            items.Add(item);
        }

        if (Window.GetWindow(item.Element) is { } window && windows.Add(window))
        {
            window.Activated += OnWindowStateChanged;
            window.Deactivated += OnWindowStateChanged;
            window.StateChanged += OnWindowStateChanged;
            window.Closed += OnWindowClosed;
        }

        StepClock clock = services.Lights.Clock.Clock;
        item.ShowStep(clock.StepAt(Stopwatch.GetTimestamp()), clock);
        Reschedule();
    }

    /// <summary>Stops animating an item (unloaded, recycled, hidden).</summary>
    /// <param name="item">The item.</param>
    public void Unregister(IStepAnimated item)
    {
        items.Remove(item);
        if (items.Count == 0)
        {
            timer.Stop();
        }
    }

    private static bool IsActive(Window? window) => window is null || (window.IsActive && window.WindowState != WindowState.Minimized);

    private void OnWindowStateChanged(object? sender, EventArgs e) => Reschedule();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        windows.Remove(window);
        window.Activated -= OnWindowStateChanged;
        window.Deactivated -= OnWindowStateChanged;
        window.StateChanged -= OnWindowStateChanged;
        window.Closed -= OnWindowClosed;
        items.RemoveAll(i => Window.GetWindow(i.Element) is null || ReferenceEquals(Window.GetWindow(i.Element), window));
        Reschedule();
    }

    private bool AnyActiveItem() => items.Exists(i => i.Element.IsVisible && IsActive(Window.GetWindow(i.Element)));

    /// <summary>Arms the timer for the next step boundary that is at least 150 ms after the last tick.</summary>
    private void Reschedule()
    {
        timer.Stop();
        if (!IsRunningPattern || PreviewActivity.IsResting(services) || !AnyActiveItem())
        {
            return;
        }

        StepClock clock = services.Lights.Clock.Clock;
        long now = Stopwatch.GetTimestamp();
        long step = clock.StepAt(now) + 1;
        long minimumTicks = (long)(MinimumInterval.TotalSeconds * Stopwatch.Frequency);
        long earliest = Math.Max(now, lastStep == long.MinValue ? now : clock.TimestampOfStep(lastStep) + minimumTicks);
        while (clock.TimestampOfStep(step) < earliest)
        {
            step++;
        }

        double milliseconds = (clock.TimestampOfStep(step) - now) * 1000.0 / Stopwatch.Frequency;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));
        timer.Start();
    }

    private void Tick()
    {
        timer.Stop();
        StepClock clock = services.Lights.Clock.Clock;
        long step = clock.StepAt(Stopwatch.GetTimestamp());
        lastStep = step;
        foreach (IStepAnimated item in items.ToArray())
        {
            if (item.Element.IsVisible && IsActive(Window.GetWindow(item.Element)) && PreviewActivity.IsInViewport(item.Element))
            {
                item.ShowStep(step, clock);
            }
        }

        Reschedule();
    }
}
