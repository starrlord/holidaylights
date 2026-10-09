namespace HolidayLights.App.Shell;

/// <summary>The orderly end of a session (PRODUCT-SPEC 2.2 item 10).</summary>
public sealed partial class AppHost
{
    /// <summary>The music fades out over 500 ms at exit.</summary>
    private static readonly TimeSpan MusicFade = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest the fades may take before the program ends anyway.</summary>
    private static readonly TimeSpan FadeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The longest the last settings write may take at exit.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private bool disposed;

    /// <summary>Stops everything in reverse order (lights fade, music fades, settings flushed, tray removed).</summary>
    /// <returns>A task that completes when everything is stopped.</returns>
    /// <remarks>A step that fails is logged and the next one still runs, so the program always ends.</remarks>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await Task.WhenAny(FadeOutAsync(), Task.Delay(FadeTimeout)).ConfigureAwait(true);
        DisposeServices();
    }

    /// <summary>Ends a session that has no message loop any more (screen saver, render test, diagnostics) on the calling thread.</summary>
    internal void ShutDownNow()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        FadeOutAsync().Wait(FadeTimeout);
        DisposeServices();
    }

    /// <summary>Writes the settings and the log at once (Windows is ending the session).</summary>
    internal void FlushForSessionEnd()
    {
        if (settings.IsValueCreated && WritesSettings && !firstRunPending)
        {
            Step("writing the settings", () => Settings.FlushAsync().Wait(FlushTimeout));
        }

        (Log as RollingFileLog)?.Flush();
    }

    /// <summary>True for sessions that may change the settings file (a screen saver or a render test never writes it).</summary>
    private bool WritesSettings => Options.Session is AppSessionKind.Normal or AppSessionKind.SettingsOnly;

    /// <summary>The lights fade out (300 ms) while the music fades out (500 ms); the returned task never faults.</summary>
    private Task FadeOutAsync()
    {
        Log.Info(LogSource, "Stopping.");
        Step("the diagnostics log", () => diagnosticsTimer?.Stop());
        Step("Automatic themes", () => scheduler?.Dispose());
        Step("the instance pipe", () => instanceServer?.Dispose());
        Step("the music rules", () =>
        {
            musicController?.Dispose();
            settingsOnlyMusic?.Dispose();
        });
        Step("the hot keys", () => DisposeIfCreated(hotKeys));
        Task lightsOut = FadeAsync("the lights", () => lights.IsValueCreated ? lights.Value.StopAsync() : Task.CompletedTask);
        Task musicOut = FadeAsync("the music", () => music.IsValueCreated ? music.Value.StopAsync(MusicFade) : Task.CompletedTask);
        return Task.WhenAll(lightsOut, musicOut);
    }

    /// <summary>The icon disappears, then every service stops in reverse order of creation and the settings are written.</summary>
    private void DisposeServices()
    {
        Step("the tray icon", () =>
        {
            tray?.Dispose();
            tray = null;
        });
        Step("the music hold", ReleaseStartupHold);
        Step("the Windows integration", () => integration?.Dispose());
        Step("the lights", () => DisposeIfCreated(lights));
        Step("the music", () => DisposeIfCreated(music));
        Step("the pause signals", () => DisposeIfCreated(pauseMonitor));
        Step("the pictures", () => DisposeIfCreated(pictures));
        Step("the songs", () => DisposeIfCreated(songs));
        Step("the bulbs", () => DisposeIfCreated(bulbs));
        if (settings.IsValueCreated && WritesSettings)
        {
            if (firstRunPending)
            {
                // Writing now would turn the next start into a later start without its newcomer or 5.4-import settings.
                Log.Warn(LogSource, "The first start did not finish; no settings file is written, so the next start runs it again.");
            }
            else
            {
                Step("the settings", () =>
                {
                    Settings.FlushAsync().Wait(FlushTimeout);
                    (Settings as IDisposable)?.Dispose();
                });
            }
        }

        Step("the holding folder", () => DisposeIfCreated(holding));
        Step("the displays", () => DisposeIfCreated(displays));
        Step("the system information", () => DisposeIfCreated(systemInfo));
        Log.Info(LogSource, "Stopped.");
        if (ownsLog)
        {
            (Log as IDisposable)?.Dispose();
        }
    }

    /// <summary>Runs one step of the shutdown; a failure is logged and the next step still runs.</summary>
    private void Step(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            Log.Error(LogSource, $"Stopping {what} failed.", e);
        }
    }

    /// <summary>Waits for a fade off the UI thread (so a blocking wait cannot deadlock); a failure is logged.</summary>
    private async Task FadeAsync(string what, Func<Task> fade)
    {
        try
        {
            await fade().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error(LogSource, $"Stopping {what} failed.", e);
        }
    }

    private static void DisposeIfCreated<T>(Lazy<T> service)
    {
        if (service.IsValueCreated && service.Value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
