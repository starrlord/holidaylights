namespace HolidayLights.Audio.Timing;

/// <summary>
/// The time base of the MIDI sequencer: <see cref="System.Diagnostics.Stopwatch"/> timestamps (the time base of
/// <see cref="MusicEvent.Timestamp"/>) and a wait that a command can interrupt. Tests substitute a fake clock.
/// </summary>
internal interface IMusicClock
{
    /// <summary>Timestamp ticks per second.</summary>
    long Frequency { get; }

    /// <summary>The current timestamp.</summary>
    long Now { get; }

    /// <summary>Blocks until <paramref name="timestamp"/> is reached or <paramref name="wake"/> is signaled.</summary>
    /// <param name="timestamp">The timestamp to wait for; <see cref="long.MaxValue"/> waits for <paramref name="wake"/> only.</param>
    /// <param name="wake">An auto-reset event that interrupts the wait (consumed when it does).</param>
    /// <returns>True when the time came, false when <paramref name="wake"/> interrupted the wait.</returns>
    bool WaitUntil(long timestamp, WaitHandle wake);
}
