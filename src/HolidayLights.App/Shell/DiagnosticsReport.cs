using System.Globalization;
using System.IO;
using System.Text;
using HolidayLights.Audio.Midi;

namespace HolidayLights.App.Shell;

/// <summary>
/// <c>--diagnostics [&lt;file&gt;]</c>: the "Copy Version Info" facts plus a check of the installation (content, settings,
/// libraries, registrations, hot keys, the scene the desktop would show), written to the file or to standard output.
/// Nothing is changed and no window is shown; every check that fails reports why instead of stopping the report.
/// </summary>
public static class DiagnosticsReport
{
    /// <summary>How long the add-on index may take.</summary>
    private static readonly TimeSpan IndexTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Writes the report.</summary>
    /// <param name="host">The composition root of a windowless session.</param>
    /// <param name="file">The output file, or null for standard output.</param>
    /// <returns>The process exit code (0).</returns>
    public static int Run(AppHost host, string? file)
    {
        ArgumentNullException.ThrowIfNull(host);
        string text = Build(host);
        if (file is not null)
        {
            File.WriteAllText(file, text, Encoding.UTF8);
        }
        else
        {
            if (!Console.IsOutputRedirected)
            {
                NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);
            }

            Console.Out.Write(text);
            Console.Out.Flush();
        }

        return 0;
    }

    /// <summary>Builds the report.</summary>
    /// <param name="host">The composition root.</param>
    /// <returns>Plain text.</returns>
    public static string Build(AppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var text = new StringBuilder();
        Line(text, "Version", () => $"Holiday Lights - Modern Edition {VersionInfo.ProgramVersion}, Windows build {host.SystemInfo.OsBuild}");
        Line(text, "Program", () => host.Paths.InstallFolder);
        Line(text, "Data root", () => host.Paths.DataRoot ?? "(normal locations)");
        Line(text, "Running instance", () => RunningInstance.IsPresent(host.Paths.DataRoot) ? "yes" : "no");
        Line(text, "Bundled content", () => string.Create(CultureInfo.InvariantCulture,
            $"{CountFiles(host.Paths.BundledBulbsFolder, "*.bul")} bulbs, {CountFiles(host.Paths.BundledMusicFolder, "*.mid")} songs, {CountFiles(host.Paths.BundledPicturesFolder, "*.bmp")} pictures"));
        Line(text, "Settings", () => $"{host.Paths.SettingsFile} ({host.Settings.LoadOutcome})");
        foreach (DisplayInfo display in host.Displays.Displays)
        {
            Line(text, $"Display {display.Number}", () =>
                $"{display.Bounds.Width} x {display.Bounds.Height} at {Math.Round(display.Scale * 100)} %, position {display.Bounds.Left}, {display.Bounds.Top}, " +
                $"work area {display.WorkArea.Width} x {display.WorkArea.Height}{(display.IsPrimary ? ", main" : "")}");
        }

        Line(text, "Bulbs", () =>
        {
            bool complete = host.CatalogIndexing.Wait(IndexTimeout);
            return $"{host.Bulbs.All.Count} listed, {host.Bulbs.DamagedFiles.Count} damaged files{(complete ? "" : ", index incomplete")}";
        });
        Line(text, "Songs", () => $"{host.Songs.Songs.Count} listed, {host.Songs.HiddenSongs.Count} removed");
        Line(text, "Pictures", () => $"{host.Pictures.Pictures.Count} listed");
        Line(text, "Themes", () => $"{host.Themes.Themes.Count} themes, {host.Themes.Problems.Count} unreadable");
        Line(text, "Scene", () => Describe(host.Lights.Scene));
        Line(text, "Matching theme", () => host.ThemeService.FindMatching(host.Settings.Current)?.Name ?? "Custom Settings");
        Line(text, "Holiday Lights 5.4", () => host.LegacyImporter.IsLegacyInstallPresent() ? "found" : "not found");
        Line(text, "Startup", () =>
            $"setting {(host.Settings.Current.Startup.Auto ? "on" : "off")}, Run value {(host.Startup.IsEnabled ? "present" : "absent")}" +
            $"{(host.Startup.IsDisabledByWindows ? ", turned off in Windows Settings" : "")}");
        Line(text, ".bul files", () => $"setting {(host.Settings.Current.Files.AssociateBul ? "on" : "off")}, {host.FileAssociation.GetState()}");
        Line(text, "Screen saver", () =>
        {
            ScreenSaverStatus status = host.ScreenSaverRegistration.GetStatus();
            return $"{status.State}, {status.ScrnsaveExe ?? "(none)"}, {status.TimeoutSeconds} s{(status.PolicyManaged ? ", set by policy" : "")}";
        });
        Line(text, "Hot keys", () => string.Join("; ", host.HotKeys.Status.Select(s =>
            $"{s.Action} {s.Binding.Format("+")} {(s.Binding.Enabled ? "on" : "off")} ({host.HotKeys.Validate(s.Binding, s.Action).Validity})")));
        Line(text, "MIDI output", () =>
        {
            IReadOnlyList<string> devices = MidiSequencer.GetDeviceNames();
            string chosen = host.Settings.Current.Music.MidiDevice;
            return $"{(chosen.Length > 0 ? chosen : "Automatic")} (devices: {(devices.Count > 0 ? string.Join(", ", devices) : "none")})";
        });
        return text.ToString();
    }

    private static string Describe(LightsScene scene) => string.Create(CultureInfo.InvariantCulture,
        $"{scene.Layout.Placements.Count} bulbs on {scene.Layout.Displays.Count} display(s), {scene.RequestedLayer}, {scene.Layout.Mode}, " +
        $"{scene.Flash.Pattern} every {scene.Interval * StepClock.TickMilliseconds} ms, {scene.Effects.Pixels}, glow {scene.Effects.Glow}, " +
        $"lights {(scene.LightsOn ? "on" : "off")}");

    private static void Line(StringBuilder text, string label, Func<string> value)
    {
        string result;
        try
        {
            result = value();
        }
        catch (Exception e)
        {
            // A diagnostics report lists what it could not find instead of failing.
            result = $"unavailable ({e.GetType().Name}: {e.Message})";
        }

        text.Append(label).Append(": ").AppendLine(result);
    }

    private static int CountFiles(string folder, string pattern) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern).Count() : 0;
}
