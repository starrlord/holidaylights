using System.Diagnostics;

namespace HolidayLights.Rendering.Engine;

/// <summary>
/// Graphics device recovery (PRODUCT-SPEC 5.13): a lost device is recreated at once; after two losses within a minute the
/// lights switch to WARP (software) for the rest of the session. When no device can be created at all, creation is retried
/// every 10 s (5.12.2 "No graphics device").
/// </summary>
internal sealed class DeviceRecoveryPolicy
{
    /// <summary>The window in which two losses switch to software drawing.</summary>
    public static readonly TimeSpan LossWindow = TimeSpan.FromMinutes(1);

    /// <summary>The retry interval while no device can be created.</summary>
    public static readonly TimeSpan UnavailableRetry = TimeSpan.FromSeconds(10);

    private readonly Queue<long> losses = new();

    /// <summary>Creates the policy.</summary>
    /// <param name="forceSoftware">Start with WARP (<see cref="LightsPresenterOptions.ForceSoftwareRendering"/>).</param>
    public DeviceRecoveryPolicy(bool forceSoftware) => UseSoftware = forceSoftware;

    /// <summary>True when the next device should be WARP.</summary>
    public bool UseSoftware { get; private set; }

    /// <summary>How many devices were lost this session.</summary>
    public int LossCount { get; private set; }

    /// <summary>Records a device loss.</summary>
    /// <param name="timestamp">When it happened (<see cref="Stopwatch.GetTimestamp"/>).</param>
    /// <returns>True when this loss switched the lights to software drawing.</returns>
    public bool RecordLoss(long timestamp)
    {
        LossCount++;
        long window = (long)(LossWindow.TotalSeconds * Stopwatch.Frequency);
        losses.Enqueue(timestamp);
        while (losses.Count > 0 && timestamp - losses.Peek() > window)
        {
            losses.Dequeue();
        }

        if (!UseSoftware && losses.Count >= 2)
        {
            UseSoftware = true;
            return true;
        }

        return false;
    }
}

/// <summary>The Lights thread is restarted after a crash at most once per minute (PRODUCT-SPEC 5.13).</summary>
internal sealed class RestartPolicy
{
    /// <summary>The minimum time between two restarts.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    private long? lastRestart;

    /// <summary>How long to wait before the next restart is allowed.</summary>
    /// <param name="now">The current timestamp.</param>
    public TimeSpan DelayBeforeRestart(long now)
    {
        if (lastRestart is not { } last)
        {
            return TimeSpan.Zero;
        }

        double elapsed = (now - last) / (double)Stopwatch.Frequency;
        double remaining = MinimumInterval.TotalSeconds - elapsed;
        return remaining > 0 ? TimeSpan.FromSeconds(remaining) : TimeSpan.Zero;
    }

    /// <summary>Records a restart.</summary>
    public void RecordRestart(long now) => lastRestart = now;
}

/// <summary>Back-off for re-creating layers after Explorer restarts: 500 ms, 1 s, 2 s, 4 s, ... up to 30 s (PRODUCT-SPEC 5.13).</summary>
internal sealed class Backoff
{
    private const double FirstMilliseconds = 500;
    private const double MaximumMilliseconds = 30_000;

    /// <summary>Attempts made since the last <see cref="Reset"/>.</summary>
    public int Attempts { get; private set; }

    /// <summary>The delay before the next attempt (and counts it).</summary>
    public TimeSpan Next()
    {
        double delay = Math.Min(MaximumMilliseconds, FirstMilliseconds * Math.Pow(2, Attempts));
        Attempts++;
        return TimeSpan.FromMilliseconds(delay);
    }

    /// <summary>Starts over (the layers are healthy again).</summary>
    public void Reset() => Attempts = 0;
}
