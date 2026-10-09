namespace HolidayLights.App.Tray;

/// <summary>How often a notification kind may be shown (PRODUCT-SPEC 3.12).</summary>
public enum NotificationLimit
{
    /// <summary>At most once per local day (remembered in <see cref="OnboardingState.LastNotified"/>).</summary>
    OncePerDay,

    /// <summary>At most once while the program runs.</summary>
    OncePerSession,

    /// <summary>Only the first time ever (<see cref="OnboardingState.CloseNotified"/>).</summary>
    OnceEver,

    /// <summary>Once per change of what it reports: the caller remembers what it told (a hot key that is in use, <see cref="NotificationThrottle.WasHotKeyReported"/>).</summary>
    OncePerChange,
}

/// <summary>The throttling rules of the tray notifications (PRODUCT-SPEC 3.12). Pure.</summary>
public static class NotificationThrottle
{
    /// <summary>The prefix of the <see cref="OnboardingState.LastNotified"/> keys that remember a hot key reported as in use.</summary>
    private const string HotKeyReportedPrefix = "HotKeyUnavailable:";

    /// <summary>
    /// The limit of a kind: "Your lights stay on" once ever, "Music is waiting" and "Lights restarted" once per session,
    /// "Hot key not available" once per change of the combination, the others once per day.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The limit.</returns>
    public static NotificationLimit LimitOf(NotificationKind kind) => kind switch
    {
        NotificationKind.FirstClose => NotificationLimit.OnceEver,
        NotificationKind.MusicWaiting or NotificationKind.LightsRestarted => NotificationLimit.OncePerSession,
        NotificationKind.HotKeyUnavailable => NotificationLimit.OncePerChange,
        _ => NotificationLimit.OncePerDay,
    };

    /// <summary>True when "Hot key not available" was already shown for this combination of the action (in any session).</summary>
    /// <param name="onboarding">The persisted state.</param>
    /// <param name="action">The hot key's job.</param>
    /// <param name="binding">The combination that is in use.</param>
    /// <returns>True when it was reported.</returns>
    public static bool WasHotKeyReported(OnboardingState onboarding, HotKeyAction action, HotKeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        ArgumentNullException.ThrowIfNull(binding);
        return onboarding.LastNotified.ContainsKey(HotKeyReportedKey(action, binding));
    }

    /// <summary>Remembers that "Hot key not available" was shown for a combination (it replaces the action's earlier one).</summary>
    /// <param name="onboarding">The state before.</param>
    /// <param name="action">The hot key's job.</param>
    /// <param name="binding">The combination that is in use.</param>
    /// <param name="today">The local date.</param>
    /// <returns>The new state.</returns>
    public static OnboardingState RecordHotKeyReported(OnboardingState onboarding, HotKeyAction action, HotKeyBinding binding, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        ArgumentNullException.ThrowIfNull(binding);
        Dictionary<string, DateOnly> days = WithoutHotKey(onboarding, action);
        days[HotKeyReportedKey(action, binding)] = today;
        return onboarding with { LastNotified = days };
    }

    /// <summary>Forgets the action's reported combination (it works now, or it was turned off): a later failure is a new change.</summary>
    /// <param name="onboarding">The state before.</param>
    /// <param name="action">The hot key's job.</param>
    /// <returns>The new state (the same instance when nothing was remembered).</returns>
    public static OnboardingState ForgetHotKeyReported(OnboardingState onboarding, HotKeyAction action)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        Dictionary<string, DateOnly> days = WithoutHotKey(onboarding, action);
        return days.Count == onboarding.LastNotified.Count ? onboarding : onboarding with { LastNotified = days };
    }

    private static string HotKeyReportedKey(HotKeyAction action, HotKeyBinding binding) => $"{HotKeyReportedPrefix}{action}:{binding.Format("+")}";

    private static Dictionary<string, DateOnly> WithoutHotKey(OnboardingState onboarding, HotKeyAction action)
    {
        string prefix = $"{HotKeyReportedPrefix}{action}:";
        return onboarding.LastNotified
            .Where(entry => !entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    /// <summary>True when a notification of the kind may be shown now.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="onboarding">The persisted throttling state.</param>
    /// <param name="shownThisSession">Kinds already shown in this session.</param>
    /// <param name="today">The local date.</param>
    /// <returns>True when allowed.</returns>
    public static bool IsAllowed(NotificationKind kind, OnboardingState onboarding, IReadOnlySet<NotificationKind> shownThisSession, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        ArgumentNullException.ThrowIfNull(shownThisSession);
        return LimitOf(kind) switch
        {
            NotificationLimit.OnceEver => !onboarding.CloseNotified,
            NotificationLimit.OncePerSession => !shownThisSession.Contains(kind),
            NotificationLimit.OncePerChange => true,
            _ => !onboarding.LastNotified.TryGetValue(KeyOf(kind), out DateOnly last) || last != today,
        };
    }

    /// <summary>The persisted state after a notification of the kind was shown.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="onboarding">The state before.</param>
    /// <param name="today">The local date.</param>
    /// <returns>The new state (the same instance when nothing is persisted for the kind).</returns>
    public static OnboardingState Record(NotificationKind kind, OnboardingState onboarding, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        switch (LimitOf(kind))
        {
            case NotificationLimit.OnceEver:
                return onboarding.CloseNotified ? onboarding : onboarding with { CloseNotified = true };
            case NotificationLimit.OncePerDay:
                var days = new Dictionary<string, DateOnly>(onboarding.LastNotified) { [KeyOf(kind)] = today };
                return onboarding with { LastNotified = days };
            default:
                return onboarding;
        }
    }

    /// <summary>The key of a kind in <see cref="OnboardingState.LastNotified"/>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The kind name, e.g. "ThemeChanged".</returns>
    public static string KeyOf(NotificationKind kind) => kind.ToString();
}
