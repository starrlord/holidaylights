using System.Globalization;
using System.Reflection;
using System.Text;

namespace HolidayLights.App.Shell;

/// <summary>What "Copy Version Info" reports (PRODUCT-SPEC 3.9).</summary>
/// <param name="Version">The program version ("6.0.0").</param>
/// <param name="OsBuild">The Windows build number.</param>
/// <param name="Displays">The connected displays.</param>
/// <param name="Requested">The requested layer mode.</param>
/// <param name="Status">The presenter's status (the effective layer mode per display).</param>
/// <param name="MidiDevice">The "MIDI Output" setting (empty = automatic).</param>
/// <param name="MidiDevices">The MIDI output devices of this PC, in device order.</param>
public sealed record VersionInfoInputs(
    string Version,
    int OsBuild,
    IReadOnlyList<DisplayInfo> Displays,
    LayerMode Requested,
    LightsStatus Status,
    string MidiDevice,
    IReadOnlyList<string> MidiDevices);

/// <summary>The "Copy Version Info" text for bug reports: version, Windows build, displays, layer modes, MIDI device. Pure.</summary>
public static class VersionInfo
{
    /// <summary>The program version without build metadata ("6.0.0").</summary>
    public static string ProgramVersion { get; } = ReadVersion();

    /// <summary>Formats the report.</summary>
    /// <param name="inputs">What to report.</param>
    /// <returns>Plain text, one fact per line.</returns>
    public static string Format(VersionInfoInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Holiday Lights - Modern Edition, version {inputs.Version}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Windows build {inputs.OsBuild}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Lights requested: {LayerText(inputs.Requested)} ({inputs.Status.Health})");
        foreach (DisplayInfo display in inputs.Displays)
        {
            DisplayLayerStatus? layer = inputs.Status.Displays.FirstOrDefault(d => string.Equals(d.DisplayId, display.DeviceId, StringComparison.Ordinal));
            string effective = layer switch
            {
                null => "no lights",
                { Resting: true } => "lights resting",
                { Effective: { } mode } => "lights " + LayerText(mode),
                _ => "lights not shown",
            };
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Display {display.Number}{(display.IsPrimary ? " (main)" : "")}: {display.Bounds.Width} x {display.Bounds.Height} at {Math.Round(display.Scale * 100)} %, " +
                $"position {display.Bounds.Left}, {display.Bounds.Top}, work area {display.WorkArea.Width} x {display.WorkArea.Height} at {display.WorkArea.Left}, {display.WorkArea.Top}; {effective}");
        }

        string midi = inputs.MidiDevice.Length > 0 ? inputs.MidiDevice : "Automatic";
        string devices = inputs.MidiDevices.Count > 0 ? string.Join(", ", inputs.MidiDevices) : "none";
        text.AppendLine(CultureInfo.InvariantCulture, $"MIDI output: {midi} (devices: {devices})");
        return text.ToString();
    }

    /// <summary>A layer mode in plain words.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>E.g. "behind the icons".</returns>
    public static string LayerText(LayerMode mode) => mode switch
    {
        LayerMode.BehindIcons => "behind the icons",
        LayerMode.InFrontOfIcons => "in front of the icons",
        _ => "on top of all windows",
    };

    private static string ReadVersion()
    {
        Assembly assembly = typeof(VersionInfo).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string version = informational ?? assembly.GetName().Version?.ToString(3) ?? "6.0.0";
        int metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata >= 0 ? version[..metadata] : version;
    }
}
