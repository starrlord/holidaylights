using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>Settings key <c>music</c>: device preferences of the Music Box (not part of themes).</summary>
public sealed record MusicSettings
{
    /// <summary>"Play Holiday Music": the global music switch (off = no music at all).</summary>
    public bool Enabled { get; set; }

    /// <summary>Volume 0-100 % (MIDI scales every channel's CC7; audio files scale their output; never the Windows mixer).</summary>
    public int Volume { get; set; } = 60;

    /// <summary>The "Mute" button of the Music Box.</summary>
    public bool Muted { get; set; }

    /// <summary>"MIDI Output" device name; empty = automatic (first device whose name contains "GS Wavetable", else device 0).</summary>
    public string MidiDevice { get; set; } = "";

    /// <summary>
    /// The way from the MIDI device to the listener (audio engine, device buffers, speakers), added with the device's own
    /// latency (GS Wavetable Synth: 190 ms) to MIDI event timestamps for "Dance to the Music" (milliseconds).
    /// </summary>
    public int SyncOffsetMs { get; set; } = 40;
}

/// <summary>"Show On" of the screen saver.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaverDisplays>))]
public enum SaverDisplays
{
    /// <summary>"All Displays" (default).</summary>
    [JsonStringEnumMemberName("all")]
    All,

    /// <summary>"Main Display Only": the other displays stay black (5.4).</summary>
    [JsonStringEnumMemberName("mainOnly")]
    MainOnly,
}

/// <summary>The Windows screen saver settings remembered before Holiday Lights became the screen saver ("Stop Using It", uninstaller).</summary>
public sealed record ScreenSaverPrevious
{
    /// <summary>The previous <c>SCRNSAVE.EXE</c> value (null or empty = none).</summary>
    public string? ScrnsaveExe { get; set; }

    /// <summary>The previous <c>ScreenSaveActive</c>.</summary>
    public bool Active { get; set; }

    /// <summary>The previous <c>ScreenSaveTimeOut</c> in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 600;
}

/// <summary>Settings key <c>saver</c>: device preferences of the screen saver.</summary>
public sealed record SaverDeviceSettings
{
    /// <summary>"Show On".</summary>
    public SaverDisplays ShowOn { get; set; } = SaverDisplays.All;

    /// <summary>Recorded only when Holiday Lights becomes the screen saver; null otherwise.</summary>
    public ScreenSaverPrevious? Previous { get; set; }
}

/// <summary>Settings key <c>colors</c>.</summary>
public sealed record ColorSettings
{
    /// <summary>Number of custom colours of the Color dialog.</summary>
    public const int CustomColorCount = 16;

    /// <summary>The 16 "Custom Colors" (imported from 5.4 "Custom Color 0-15"; part of the Cancel snapshot).</summary>
    public IReadOnlyList<RgbColor> Custom { get; set; } = Enumerable.Repeat(RgbColor.Black, CustomColorCount).ToArray();
}

/// <summary>Hot key modifier keys (values are the Win32 <c>MOD_*</c> flags).</summary>
[Flags]
[JsonConverter(typeof(HotKeyModifiersJsonConverter))]
public enum HotKeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Alt (<c>MOD_ALT</c>).</summary>
    Alt = 0x1,

    /// <summary>Ctrl (<c>MOD_CONTROL</c>).</summary>
    Ctrl = 0x2,

    /// <summary>Shift (<c>MOD_SHIFT</c>).</summary>
    Shift = 0x4,

    /// <summary>Windows key (<c>MOD_WIN</c>).</summary>
    Win = 0x8,
}

/// <summary>Writes <see cref="HotKeyModifiers"/> as a JSON array in the order Ctrl, Alt, Shift, Win (Appendix C: <c>["Ctrl", "Alt", "Shift"]</c>).</summary>
public sealed class HotKeyModifiersJsonConverter : JsonConverter<HotKeyModifiers>
{
    private static readonly (HotKeyModifiers Flag, string Name)[] Order =
        [(HotKeyModifiers.Ctrl, "Ctrl"), (HotKeyModifiers.Alt, "Alt"), (HotKeyModifiers.Shift, "Shift"), (HotKeyModifiers.Win, "Win")];

    /// <inheritdoc />
    public override HotKeyModifiers Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected an array of modifier names.");
        }

        var result = HotKeyModifiers.None;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            string? name = reader.GetString();
            (HotKeyModifiers flag, _) = Order.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            result |= flag == HotKeyModifiers.None ? throw new JsonException($"Unknown modifier '{name}'.") : flag;
        }

        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, HotKeyModifiers value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach ((HotKeyModifiers flag, string name) in Order)
        {
            if (value.HasFlag(flag))
            {
                writer.WriteStringValue(name);
            }
        }

        writer.WriteEndArray();
    }
}

/// <summary>One global hot key (PRODUCT-SPEC 3.7, 3.8.2, 6.6.4).</summary>
public sealed record HotKeyBinding
{
    /// <summary>The check box: registered at once when on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Modifier keys.</summary>
    public HotKeyModifiers Modifiers { get; set; } = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift;

    /// <summary>
    /// The key: "A"-"Z", "0"-"9", "NumPad0"-"NumPad9", "F1"-"F24", "Insert", "Home", "End", "PageUp", "PageDown" or "Pause"
    /// (the keys the Change Hot Key dialog accepts).
    /// </summary>
    public string Key { get; set; } = "B";

    /// <summary>The key caps as the UI shows them, e.g. "Ctrl + Alt + Shift + B" (separator " + ") or "Ctrl+Alt+Shift+B" (separator "+").</summary>
    /// <param name="separator">Text between the caps.</param>
    /// <returns>The formatted combination.</returns>
    public string Format(string separator = " + ")
    {
        var caps = new List<string>(5);
        if (Modifiers.HasFlag(HotKeyModifiers.Ctrl))
        {
            caps.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            caps.Add("Alt");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            caps.Add("Shift");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Win))
        {
            caps.Add("Win");
        }

        caps.Add(Key);
        return string.Join(separator, caps);
    }
}

/// <summary>Settings key <c>hotkeys</c>.</summary>
public sealed record HotKeySettings
{
    /// <summary>"Switch Between On Desktop and On Top" (the 5.4 job): on, Ctrl+Alt+Shift+B (imports keep the 5.4 letter and on/off).</summary>
    public HotKeyBinding Location { get; set; } = new() { Enabled = true, Key = "B" };

    /// <summary>"Turn the Lights On or Off": off, Ctrl+Alt+Shift+L.</summary>
    public HotKeyBinding Lights { get; set; } = new() { Enabled = false, Key = "L" };
}

/// <summary>Settings key <c>startup</c>.</summary>
public sealed record StartupSettings
{
    /// <summary>"Automatically Start Holiday Lights" (per-user Run value, applied at once).</summary>
    public bool Auto { get; set; } = true;
}

/// <summary>"When Energy Saver Is On:" (PRODUCT-SPEC 5.12.3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EnergySaverChoice>))]
public enum EnergySaverChoice
{
    /// <summary>"Use Less Power" (default): glow off, fading off, step period at least 300 ms; the lights keep flashing.</summary>
    [JsonStringEnumMemberName("useLessPower")]
    UseLessPower,

    /// <summary>"Stop Flashing": every bulb shows frame 0 (lit), no glow, no fading, no clock.</summary>
    [JsonStringEnumMemberName("stopFlashing")]
    StopFlashing,

    /// <summary>"Turn Off the Lights": as Lights Off until Energy Saver ends.</summary>
    [JsonStringEnumMemberName("turnOffLights")]
    TurnOffLights,

    /// <summary>"Change Nothing".</summary>
    [JsonStringEnumMemberName("changeNothing")]
    ChangeNothing,
}

/// <summary>Settings key <c>rest</c>: "When the Lights Rest".</summary>
public sealed record RestSettings
{
    /// <summary>"Hide the Lights on a Display While a Full-Screen App or Game Is Running There".</summary>
    public bool FullScreen { get; set; } = true;

    /// <summary>"Hide the Lights During Presentations".</summary>
    public bool Presentation { get; set; } = true;

    /// <summary>"When Energy Saver Is On:".</summary>
    public EnergySaverChoice EnergySaver { get; set; } = EnergySaverChoice.UseLessPower;

    /// <summary>"Pause Music While a Full-Screen App, Game or Presentation Is Running".</summary>
    public bool MusicFullScreen { get; set; } = true;

    /// <summary>"Pause Music During Focus Sessions".</summary>
    public bool MusicFocus { get; set; } = true;

    /// <summary>"Pause Music When I Lock My PC".</summary>
    public bool MusicLock { get; set; } = true;
}

/// <summary>Settings key <c>accessibility</c>.</summary>
public sealed record AccessibilitySettings
{
    /// <summary>"Limit Flashing to 3 Flashes per Second" (on by default when Windows animation effects are off at first run).</summary>
    public bool LimitFlashing { get; set; }
}

/// <summary>Settings key <c>files</c>.</summary>
public sealed record FileSettings
{
    /// <summary>"Open Bulb Files (.bul) with Holiday Lights" (per-user association).</summary>
    public bool AssociateBul { get; set; } = true;
}
