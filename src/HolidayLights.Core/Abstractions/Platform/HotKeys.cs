namespace HolidayLights.Core.Abstractions;

/// <summary>The two global hot keys (PRODUCT-SPEC 6.6.4).</summary>
public enum HotKeyAction
{
    /// <summary>"Switch Between On Desktop and On Top" (the 5.4 job).</summary>
    SwitchLocation,

    /// <summary>"Turn the Lights On or Off".</summary>
    ToggleLights,
}

/// <summary>Result of registering a hot key.</summary>
public enum HotKeyRegistration
{
    /// <summary>Registered with <c>RegisterHotKey(MOD_NOREPEAT)</c>.</summary>
    Registered,

    /// <summary>Another program owns the combination ("Another program is using this key combination. Choose another one.").</summary>
    InUse,

    /// <summary>Refused because it types a character with AltGr on an installed keyboard layout (3.8.2), or the key is not allowed.</summary>
    Unsafe,
}

/// <summary>The message line of the Change Hot Key dialog (PRODUCT-SPEC 3.8.2), first that applies.</summary>
public enum HotKeyValidity
{
    /// <summary>The combination can be saved without a note.</summary>
    Ok,

    /// <summary>The key is not one of the accepted keys (Esc cannot be a hot key). Save disabled.</summary>
    KeyNotAllowed,

    /// <summary>No Ctrl, Alt or Windows and the key is not F1-F24 or Pause. Save disabled.</summary>
    NeedsModifier,

    /// <summary>Shift plus a letter alone: "That would stop you from typing capital letters." Save disabled.</summary>
    ShiftLetterOnly,

    /// <summary>"Another program is using this combination." (a trial <c>RegisterHotKey</c> failed). Save disabled.</summary>
    InUse,

    /// <summary>"This combination types "Ł" on your Polish (Programmers) keyboard. Choose another." Save disabled.</summary>
    TypesCharacter,

    /// <summary>Ctrl+Shift+B/T/N/Delete: a browser shortcut warning; Save stays enabled.</summary>
    BrowserShortcut,

    /// <summary>"Windows uses many Windows-key shortcuts; this one might stop working." Save stays enabled.</summary>
    WindowsKeyCombination,
}

/// <summary>Verdict of <see cref="IHotKeyService.Validate"/>.</summary>
/// <param name="Validity">The first rule that applies.</param>
/// <param name="TypedCharacter">For <see cref="HotKeyValidity.TypesCharacter"/>: the character produced.</param>
/// <param name="LayoutName">For <see cref="HotKeyValidity.TypesCharacter"/>: the keyboard layout's display name.</param>
public sealed record HotKeyCheck(HotKeyValidity Validity, string? TypedCharacter = null, string? LayoutName = null)
{
    /// <summary>True when Save is allowed (no refusal; warnings allowed).</summary>
    public bool CanSave => Validity is HotKeyValidity.Ok or HotKeyValidity.BrowserShortcut or HotKeyValidity.WindowsKeyCombination;
}

/// <summary>Arguments of <see cref="IHotKeyService.Pressed"/>.</summary>
public sealed class HotKeyPressedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="action">The hot key that was pressed.</param>
    public HotKeyPressedEventArgs(HotKeyAction action) => Action = action;

    /// <summary>The hot key that was pressed.</summary>
    public HotKeyAction Action { get; }
}

/// <summary>
/// Global hot keys on a hidden top-level message window of the creating (UI) thread. Implemented by platform. Registered
/// in normal sessions only (never in screen saver or settings-only sessions).
/// </summary>
public interface IHotKeyService : IDisposable
{
    /// <summary>Raised on the UI thread when a registered hot key is pressed.</summary>
    event EventHandler<HotKeyPressedEventArgs>? Pressed;

    /// <summary>Raised when the installed keyboard layouts changed (re-check AltGr safety).</summary>
    event EventHandler? KeyboardLayoutsChanged;

    /// <summary>Registers (or re-registers) a hot key; an unsafe combination is not registered.</summary>
    /// <param name="action">Which hot key.</param>
    /// <param name="binding">The combination (its Enabled flag is ignored).</param>
    /// <returns>The outcome.</returns>
    HotKeyRegistration Register(HotKeyAction action, HotKeyBinding binding);

    /// <summary>Unregisters a hot key (no-op when not registered).</summary>
    /// <param name="action">Which hot key.</param>
    void Unregister(HotKeyAction action);

    /// <summary>Checks a combination for the Change Hot Key dialog and at start-up (AltGr check against every layout of <c>GetKeyboardLayoutList</c>).</summary>
    /// <param name="binding">The candidate combination.</param>
    /// <param name="action">The hot key it is for (its own current registration does not count as "in use").</param>
    /// <returns>The verdict.</returns>
    HotKeyCheck Validate(HotKeyBinding binding, HotKeyAction action);
}
