namespace HolidayLights.App.FirstRun;

/// <summary>Which Welcome card to show (PRODUCT-SPEC 3.11).</summary>
public enum WelcomeVariant
{
    /// <summary>Newcomer: "Your desktop is decorated!".</summary>
    Newcomer,

    /// <summary>5.4 at factory defaults: "Welcome back!" with "Use My 2003 Lights".</summary>
    Imported54FactoryDefaults,

    /// <summary>Customized 5.4: "Welcome back!" with "Show Import Details" and "Start Fresh Instead".</summary>
    Imported54Customized,
}

/// <summary>
/// The Welcome card entry point (owner: app-shell): a small non-modal Mica window, shown once about 2.5 s after the
/// first-run power-up, never at sign-in; it never blocks the lights.
/// </summary>
public static class WelcomeCard
{
    /// <summary>Shows the card.</summary>
    /// <param name="services">The services.</param>
    /// <param name="variant">The variant.</param>
    /// <returns>A task that completes when the card closed (the held first song may start then).</returns>
    public static async Task ShowAsync(IAppServices services, WelcomeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(services);
        WelcomeSituation situation = await Task.Run(() => WelcomeSituation.Detect(services, variant)).ConfigureAwait(true);
        var window = new WelcomeWindow(services, variant, situation, TimeProvider.System);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        services.Log.Info("FirstRun", $"Welcome card: {variant}.");

        // The user just started the program, so the card may take the focus.
        window.Show();
        window.Activate();
        await closed.Task.ConfigureAwait(true);
    }
}
