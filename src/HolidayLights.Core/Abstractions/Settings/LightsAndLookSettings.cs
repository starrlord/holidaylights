using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>"Bulb Drawing" (5.4 "Bulb Location"): On Desktop or On Top.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BulbDrawing>))]
public enum BulbDrawing
{
    /// <summary>"On Desktop" (default; 5.4 value 0): behind or in front of the icons per "Behind the Desktop Icons".</summary>
    [JsonStringEnumMemberName("desktop")]
    Desktop,

    /// <summary>"On Top" (5.4 non-zero): on top of all windows, below the taskbar.</summary>
    [JsonStringEnumMemberName("onTop")]
    OnTop,
}

/// <summary>Per-display on/off (General > Displays).</summary>
public sealed record DisplaySelection
{
    /// <summary>Device ids (<see cref="DisplayInfo.DeviceId"/>) of displays without lights; every other display, including a newly connected one, is on. At least one display stays on.</summary>
    public IReadOnlyList<string> Disabled { get; set; } = [];
}

/// <summary>Settings key <c>lights</c>.</summary>
public sealed record LightsSettings
{
    /// <summary>"Show Lights" (tray, Home, hot key). Persisted; not part of the Cancel snapshot.</summary>
    public bool On { get; set; } = true;

    /// <summary>"Bulb Drawing".</summary>
    public BulbDrawing Drawing { get; set; } = BulbDrawing.Desktop;

    /// <summary>"Behind the Desktop Icons" (only meaningful with <see cref="BulbDrawing.Desktop"/>).</summary>
    public bool BehindIcons { get; set; } = true;

    /// <summary>Per-display on/off.</summary>
    public DisplaySelection Displays { get; set; } = new();

    /// <summary>"Frame:" Each Display or All Displays Together.</summary>
    public FrameMode FrameMode { get; set; } = FrameMode.EachDisplay;

    /// <summary>"Bulb Size".</summary>
    public BulbSize Size { get; set; } = BulbSize.Standard;

    /// <summary>The pattern to restore when "Make the Lights Dance to the Music" is unchecked (PRODUCT-SPEC 3.4.5); null = Flash Together.</summary>
    public FlashPatternId? PatternBeforeDance { get; set; }
}

/// <summary>Settings key <c>look</c>: how bulbs are drawn (not part of themes).</summary>
public sealed record LookSettings
{
    /// <summary>"Pixels": Smooth or Crisp.</summary>
    public SpriteStyle Pixels { get; set; } = SpriteStyle.Smooth;

    /// <summary>"Glow": Off, Soft or Bright.</summary>
    public GlowLevel Glow { get; set; } = GlowLevel.Soft;

    /// <summary>"Smooth Fading" (light bulbs fade on and off).</summary>
    public bool SmoothFading { get; set; } = true;

    /// <summary>"Smooth Screen Saver Motion" / "Smooth Motion" (interpolated screen saver motion at the refresh rate).</summary>
    public bool SmoothSaverMotion { get; set; } = true;
}

/// <summary>The "Look" presets (PRODUCT-SPEC 3.7, D12). The preset is derived from <see cref="LookSettings"/>, never stored.</summary>
public enum LookPreset
{
    /// <summary>"Modern Glow" (default): Smooth, Soft glow, Smooth Fading on, Smooth Screen Saver Motion on.</summary>
    ModernGlow,

    /// <summary>"Bright Glow": Smooth, Bright glow, on, on.</summary>
    BrightGlow,

    /// <summary>"Classic 2003": Crisp, glow Off, fading off, motion off - exactly like 2003.</summary>
    Classic2003,

    /// <summary>"Custom": selected automatically when the four values match no preset; choosing it changes nothing.</summary>
    Custom,
}

/// <summary>Maps between <see cref="LookPreset"/> and <see cref="LookSettings"/>.</summary>
public static class LookPresets
{
    /// <summary>The four values of a preset.</summary>
    /// <param name="preset">A preset other than <see cref="LookPreset.Custom"/>.</param>
    /// <returns>The values, or null for <see cref="LookPreset.Custom"/>.</returns>
    public static LookSettings? ValuesOf(LookPreset preset) => preset switch
    {
        LookPreset.ModernGlow => new LookSettings { Pixels = SpriteStyle.Smooth, Glow = GlowLevel.Soft, SmoothFading = true, SmoothSaverMotion = true },
        LookPreset.BrightGlow => new LookSettings { Pixels = SpriteStyle.Smooth, Glow = GlowLevel.Bright, SmoothFading = true, SmoothSaverMotion = true },
        LookPreset.Classic2003 => new LookSettings { Pixels = SpriteStyle.Crisp, Glow = GlowLevel.Off, SmoothFading = false, SmoothSaverMotion = false },
        LookPreset.Custom => null,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    /// <summary>The preset whose four values equal <paramref name="look"/>, else <see cref="LookPreset.Custom"/>.</summary>
    /// <param name="look">The current look.</param>
    /// <returns>The matching preset.</returns>
    public static LookPreset Detect(LookSettings look)
    {
        foreach (LookPreset preset in new[] { LookPreset.ModernGlow, LookPreset.BrightGlow, LookPreset.Classic2003 })
        {
            if (ValuesOf(preset) == look)
            {
                return preset;
            }
        }

        return LookPreset.Custom;
    }
}
