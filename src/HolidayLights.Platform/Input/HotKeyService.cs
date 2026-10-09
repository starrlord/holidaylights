using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Input;

/// <summary>
/// <c>RegisterHotKey(MOD_NOREPEAT)</c> on a hidden top-level message window, the Change Hot Key rules and the AltGr check
/// with <c>ToUnicodeEx</c> over <c>GetKeyboardLayoutList</c> (see <see cref="IHotKeyService"/>). Owner: platform.
/// </summary>
/// <remarks>
/// Create and use it on the UI thread (Windows binds a hot key to the thread of its window). Keyboard layout changes are
/// noticed through registry notifications and <c>WM_SETTINGCHANGE</c>, re-checked 2 s later (a new layout is loaded
/// shortly after its registry entry), and reported only when the layout list really changed.
/// </remarks>
public sealed class HotKeyService : IHotKeyService
{
    private const string LogSource = "Platform.HotKeys";
    private const int TrialId = 0x7F00;
    private const uint LayoutsMaybeChangedMessage = NativeMethods.WM_APP + 1;
    private const nuint LayoutRecheckTimerId = 1;
    private static readonly TimeSpan LayoutRecheckDelay = TimeSpan.FromSeconds(2);

    private readonly IAppLog log;
    private readonly MessageWindow window;
    private readonly KeyboardLayoutWatcher watcher;
    private readonly Dictionary<HotKeyAction, HotKeyBinding> registered = [];
    private IReadOnlyList<nint> layouts;

    /// <summary>Creates the message window on the calling (UI) thread.</summary>
    /// <param name="log">The log.</param>
    public HotKeyService(IAppLog log)
    {
        this.log = log;
        layouts = KeyboardLayouts.Installed();
        window = new MessageWindow("Holiday Lights Hot Keys", OnMessage, log, LogSource);
        watcher = new KeyboardLayoutWatcher(() => window.Post(LayoutsMaybeChangedMessage), log);
    }

    /// <inheritdoc />
    public event EventHandler<HotKeyPressedEventArgs>? Pressed;

    /// <inheritdoc />
    public event EventHandler? KeyboardLayoutsChanged;

    /// <inheritdoc />
    public HotKeyRegistration Register(HotKeyAction action, HotKeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        EnsureOwnerThread();
        Unregister(action);

        if (HotKeyRules.CheckShape(binding, out uint virtualKey) is { } refusal)
        {
            log.Warn(LogSource, $"{action} hot key {binding.Format("+")} not registered: {refusal}.");
            return HotKeyRegistration.Unsafe;
        }

        if (HotKeyRules.UsesAltGr(binding.Modifiers) && KeyboardLayouts.FindAltGrCharacter(virtualKey, binding.Modifiers) is { } typed)
        {
            log.Warn(LogSource, $"{action} hot key {binding.Format("+")} not registered: it types a character on the {typed.LayoutName} keyboard.");
            return HotKeyRegistration.Unsafe;
        }

        if (!NativeMethods.RegisterHotKey(window.Handle, IdOf(action), NativeModifiers(binding) | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            log.Warn(LogSource, $"{action} hot key {binding.Format("+")} is used by another program.");
            return HotKeyRegistration.InUse;
        }

        registered[action] = binding;
        log.Info(LogSource, $"{action} hot key {binding.Format("+")} registered.");
        return HotKeyRegistration.Registered;
    }

    /// <inheritdoc />
    public void Unregister(HotKeyAction action)
    {
        EnsureOwnerThread();
        if (registered.Remove(action))
        {
            NativeMethods.UnregisterHotKey(window.Handle, IdOf(action));
        }
    }

    /// <inheritdoc />
    public HotKeyCheck Validate(HotKeyBinding binding, HotKeyAction action)
    {
        ArgumentNullException.ThrowIfNull(binding);
        EnsureOwnerThread();
        return HotKeyRules.Validate(binding, virtualKey => IsInUse(binding, virtualKey, action), KeyboardLayouts.FindAltGrCharacter);
    }

    /// <summary>Unregisters everything and destroys the window.</summary>
    public void Dispose()
    {
        watcher.Dispose();
        if (window.Handle != 0 && NativeMethods.GetCurrentThreadId() == window.OwnerThreadId)
        {
            foreach (HotKeyAction action in registered.Keys)
            {
                NativeMethods.UnregisterHotKey(window.Handle, IdOf(action));
            }
        }

        // From another thread, destroying the window (on its own thread) releases its hot keys.
        registered.Clear();
        window.Dispose();
    }

    private static int IdOf(HotKeyAction action) => (int)action + 1;

    private static uint NativeModifiers(HotKeyBinding binding) => (uint)binding.Modifiers & 0xF;

    private static bool SameCombination(HotKeyBinding a, HotKeyBinding b) =>
        a.Modifiers == b.Modifiers && string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>A trial registration (its own current combination does not count as in use).</summary>
    private bool IsInUse(HotKeyBinding binding, uint virtualKey, HotKeyAction action)
    {
        if (registered.TryGetValue(action, out HotKeyBinding? own) && SameCombination(own, binding))
        {
            return false;
        }

        if (!NativeMethods.RegisterHotKey(window.Handle, TrialId, NativeModifiers(binding) | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            return true;
        }

        NativeMethods.UnregisterHotKey(window.Handle, TrialId);
        return false;
    }

    private void EnsureOwnerThread()
    {
        if (NativeMethods.GetCurrentThreadId() != window.OwnerThreadId)
        {
            throw new InvalidOperationException("Hot keys must be registered on the thread that created the hot key service.");
        }
    }

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (message)
        {
            case NativeMethods.WM_HOTKEY:
                int id = (int)wParam;
                if (id != TrialId && Enum.IsDefined((HotKeyAction)(id - 1)) && registered.ContainsKey((HotKeyAction)(id - 1)))
                {
                    Pressed?.Invoke(this, new HotKeyPressedEventArgs((HotKeyAction)(id - 1)));
                }

                return true;
            case LayoutsMaybeChangedMessage:
            case NativeMethods.WM_SETTINGCHANGE:
                CheckLayouts();
                window.StartTimer(LayoutRecheckTimerId, LayoutRecheckDelay);
                return message == LayoutsMaybeChangedMessage;
            case NativeMethods.WM_TIMER when wParam == (nint)LayoutRecheckTimerId:
                window.StopTimer(LayoutRecheckTimerId);
                CheckLayouts();
                return true;
            default:
                return false;
        }
    }

    private void CheckLayouts()
    {
        IReadOnlyList<nint> current = KeyboardLayouts.Installed();
        if (current.SequenceEqual(layouts))
        {
            return;
        }

        layouts = current;
        log.Info(LogSource, $"Keyboard layouts changed ({current.Count} installed).");
        KeyboardLayoutsChanged?.Invoke(this, EventArgs.Empty);
    }
}
