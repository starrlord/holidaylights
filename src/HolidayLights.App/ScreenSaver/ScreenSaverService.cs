using System.Windows;
using HolidayLights.App.ScreenSaver.FullScreen;
using HolidayLights.App.ScreenSaver.Preview;
using HolidayLights.App.ScreenSaver.Scene;

namespace HolidayLights.App.ScreenSaver;

/// <summary>The screen saver for the Settings window (see <see cref="IScreenSaverService"/>). Owner: screensaver.</summary>
public sealed class ScreenSaverService : IScreenSaverService
{
    private const string LogSource = "ScreenSaver";

    private readonly IAppServices services;
    private Task? preview;

    /// <summary>Creates the service.</summary>
    /// <param name="services">The application services.</param>
    public ScreenSaverService(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    /// <inheritdoc />
    public bool IsPreviewRunning => preview is { IsCompleted: false };

    /// <inheritdoc />
    public FrameworkElement CreatePreview() =>
        new SaverPreviewElement(SaverServices.From(services), services.Settings, services.Displays, services.SystemInfo, services.Music.Events);

    /// <inheritdoc />
    /// <remarks>
    /// While it runs, <see cref="IScreenSaverSessions"/> rests the desktop lights and lets the music follow "Play the Chosen
    /// Songs" as if the saver were running; "Dance to the Music" follows the app's music. Windows' screen saver settings are
    /// never touched (5.4 installed itself here). A second call while it runs returns the running preview. When the saver
    /// cannot start, whatever it opened is closed, the session is reported as stopped (the desktop lights come back) and the
    /// returned task fails with the error, so the page can say so and the button works again.
    /// </remarks>
    public Task RunPreviewAsync()
    {
        if (preview is { IsCompleted: false } running)
        {
            return running;
        }

        var done = new TaskCompletionSource();
        preview = done.Task;
        bool stopped = false;
        void Stopped()
        {
            if (!stopped)
            {
                stopped = true;
                Notify(services.SaverSessions.SaverStopped);
            }
        }

        FullScreenSaver? saver = null;
        Notify(services.SaverSessions.SaverStarted);
        try
        {
            saver = new FullScreenSaver(SaverServices.From(services));
            saver.Ended += (_, _) =>
            {
                Stopped();
                done.TrySetResult();
            };
            saver.AttachMusic(services.Music.Events);
            saver.Start(services.Settings.Current, services.Displays.Displays);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "Preview Screen Saver couldn't start.", ex);
            done.TrySetException(ex);
            EndAfterFailure(saver);
            Stopped();
        }

        return done.Task;
    }

    /// <summary>Closes whatever a saver that failed to start had opened (its end is reported through <c>Ended</c>).</summary>
    private void EndAfterFailure(FullScreenSaver? saver)
    {
        try
        {
            saver?.End();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The screen saver couldn't close after a failed start.", ex);
        }
    }

    /// <summary>Tells the session tracker; a failure there must not stop the preview.</summary>
    private void Notify(Action sessionChange)
    {
        try
        {
            sessionChange();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The screen saver session couldn't be reported.", ex);
        }
    }
}
