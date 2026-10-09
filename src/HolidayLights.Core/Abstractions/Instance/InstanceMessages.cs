using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>Commands a later launch or the screen saver process sends to the running instance (PRODUCT-SPEC 6.6.2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InstanceCommandKind>))]
public enum InstanceCommandKind
{
    /// <summary><c>show-settings [page]</c>: open Settings (no argument: the last page).</summary>
    [JsonStringEnumMemberName("show-settings")]
    ShowSettings,

    /// <summary><c>open &lt;files&gt;</c>: add <c>.bul</c>/<c>.gif</c> files (3.2.10).</summary>
    [JsonStringEnumMemberName("open")]
    Open,

    /// <summary><c>toggle-lights</c>: Show Lights on/off.</summary>
    [JsonStringEnumMemberName("toggle-lights")]
    ToggleLights,

    /// <summary><c>lights on|off</c>.</summary>
    [JsonStringEnumMemberName("lights")]
    Lights,

    /// <summary><c>toggle-layer</c>: same as the location hot key.</summary>
    [JsonStringEnumMemberName("toggle-layer")]
    ToggleLayer,

    /// <summary><c>theme &lt;name&gt;</c>: load a theme.</summary>
    [JsonStringEnumMemberName("theme")]
    Theme,

    /// <summary><c>exit</c>: exit the running instance.</summary>
    [JsonStringEnumMemberName("exit")]
    Exit,

    /// <summary><c>reset</c>: open Settings on General and ask "Reset Holiday Lights?" (forwarded <c>--reset</c>).</summary>
    [JsonStringEnumMemberName("reset")]
    Reset,

    /// <summary><c>saver-started</c>: the Holiday Lights screen saver runs; rest the desktop layers, play music per mode, stream music events back on this connection.</summary>
    [JsonStringEnumMemberName("saver-started")]
    SaverStarted,

    /// <summary><c>saver-stopped</c>: the screen saver ended (also implied when its connection closes).</summary>
    [JsonStringEnumMemberName("saver-stopped")]
    SaverStopped,

    /// <summary><c>subscribe-music-events</c>: stream <see cref="MusicEvent"/>s back on this connection until it closes.</summary>
    [JsonStringEnumMemberName("subscribe-music-events")]
    SubscribeMusicEvents,
}

/// <summary>One command with its arguments.</summary>
public sealed record InstanceCommand
{
    /// <summary>The command.</summary>
    public InstanceCommandKind Kind { get; set; }

    /// <summary>Arguments (page name, file paths, "on"/"off", theme name).</summary>
    public IReadOnlyList<string> Arguments { get; set; } = [];

    /// <summary><c>show-settings [page]</c>.</summary>
    /// <param name="page">The page, or null for the last page.</param>
    /// <returns>The command.</returns>
    public static InstanceCommand ShowSettings(SettingsPageId? page = null) =>
        new() { Kind = InstanceCommandKind.ShowSettings, Arguments = page is { } p ? [PageName(p)] : [] };

    /// <summary><c>open &lt;files&gt;</c>.</summary>
    /// <param name="paths">Full paths of the files.</param>
    /// <returns>The command.</returns>
    public static InstanceCommand Open(IEnumerable<string> paths) => new() { Kind = InstanceCommandKind.Open, Arguments = [.. paths] };

    /// <summary><c>lights on|off</c>.</summary>
    /// <param name="on">True for on.</param>
    /// <returns>The command.</returns>
    public static InstanceCommand Lights(bool on) => new() { Kind = InstanceCommandKind.Lights, Arguments = [on ? "on" : "off"] };

    /// <summary><c>theme &lt;name&gt;</c>.</summary>
    /// <param name="name">The theme name.</param>
    /// <returns>The command.</returns>
    public static InstanceCommand Theme(string name) => new() { Kind = InstanceCommandKind.Theme, Arguments = [name] };

    /// <summary>A command without arguments.</summary>
    /// <param name="kind">The command.</param>
    /// <returns>The command.</returns>
    public static InstanceCommand Simple(InstanceCommandKind kind) => new() { Kind = kind };

    /// <summary>The page argument of <see cref="ShowSettings"/>: home, bulbs, music, saver, themes, general.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The command-line name.</returns>
    public static string PageName(SettingsPageId page) => page switch
    {
        SettingsPageId.Home => "home",
        SettingsPageId.BulbFactory => "bulbs",
        SettingsPageId.MusicBox => "music",
        SettingsPageId.ScreenSaver => "saver",
        SettingsPageId.Themes => "themes",
        SettingsPageId.General => "general",
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };

    /// <summary>Parses a page name (case-insensitive).</summary>
    /// <param name="name">home, bulbs, music, saver, themes or general.</param>
    /// <param name="page">The page.</param>
    /// <returns>True when the name is known.</returns>
    public static bool TryParsePageName(string? name, out SettingsPageId page)
    {
        foreach (SettingsPageId candidate in Enum.GetValues<SettingsPageId>())
        {
            if (string.Equals(PageName(candidate), name, StringComparison.OrdinalIgnoreCase))
            {
                page = candidate;
                return true;
            }
        }

        page = default;
        return false;
    }
}

/// <summary>
/// One line on the instance pipe: a command (client to server), a music event (server to a subscribed screen saver) or an
/// acknowledgement. Exactly one member is set. Wire format: one UTF-8 JSON object per line (<see cref="ToLine"/>).
/// </summary>
public sealed record InstanceMessage
{
    /// <summary>A command.</summary>
    public InstanceCommand? Command { get; set; }

    /// <summary>A music event.</summary>
    public MusicEvent? Music { get; set; }

    /// <summary>The server's answer to a command: true when it was accepted.</summary>
    public bool? Accepted { get; set; }

    /// <summary>Serializes the message as one line (no line breaks inside).</summary>
    /// <returns>The JSON line without the terminating newline.</returns>
    public string ToLine() => JsonSerializer.Serialize(this, HolidayLightsJsonContext.Compact.InstanceMessage);

    /// <summary>Parses one line.</summary>
    /// <param name="line">A JSON line.</param>
    /// <param name="message">The message when the line is valid.</param>
    /// <returns>True when the line is a valid message.</returns>
    public static bool TryParseLine(string? line, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out InstanceMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            message = JsonSerializer.Deserialize(line, HolidayLightsJsonContext.Compact.InstanceMessage);
        }
        catch (JsonException)
        {
            return false;
        }

        return message is not null;
    }

    /// <summary>Formats the message for logs.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() => Command is { } c
        ? string.Create(CultureInfo.InvariantCulture, $"command {c.Kind} ({c.Arguments.Count} arguments)")
        : Music is { } m ? $"music {m.Kind}" : $"accepted={Accepted}";
}

/// <summary>Names of the single-instance objects (PRODUCT-SPEC 6.6.2).</summary>
public static class InstanceNames
{
    /// <summary>The mutex the first normal instance takes. Screen saver processes never take it.</summary>
    public const string Mutex = @"Local\HolidayLights6.Instance";

    /// <summary>The per-user pipe name prefix; the full name is the prefix plus the user's SID (<c>PipeOptions.CurrentUserOnly</c>).</summary>
    public const string PipePrefix = "HolidayLights6.";

    /// <summary>
    /// The mutex of a session: <see cref="Mutex"/>, or, with a data root (<c>--data-root</c> or
    /// <see cref="DataPaths.DataRootVariable"/>), the mutex plus a short stable hash of the full data root, so an isolated
    /// session never reaches another session or the user's real instance (CONTRACTS 1.8, PO-3).
    /// </summary>
    /// <param name="dataRoot">The data root (<see cref="DataPaths.DataRoot"/>), or null for the normal locations.</param>
    /// <returns>The mutex name.</returns>
    public static string MutexFor(string? dataRoot) => dataRoot is null ? Mutex : $"{Mutex}.{RootTag(dataRoot)}";

    /// <summary>The pipe name prefix of a session (the user's SID follows); see <see cref="MutexFor"/>.</summary>
    /// <param name="dataRoot">The data root, or null for the normal locations.</param>
    /// <returns>The pipe name prefix.</returns>
    public static string PipePrefixFor(string? dataRoot) => dataRoot is null ? PipePrefix : $"{PipePrefix}{RootTag(dataRoot)}.";

    /// <summary>16 hex digits of SHA-256 over the full, case-folded data root without a trailing separator.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <returns>The tag.</returns>
    public static string RootTag(string dataRoot)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)).ToUpperInvariant();
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full));
        return Convert.ToHexString(hash, 0, 8);
    }
}

/// <summary>One connection on the instance pipe (duplex, line-based).</summary>
public interface IInstanceConnection : IAsyncDisposable
{
    /// <summary>Sends one message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the line was written.</returns>
    ValueTask SendAsync(InstanceMessage message, CancellationToken cancellationToken = default);

    /// <summary>Reads messages until the other side closes the connection (invalid lines are skipped).</summary>
    /// <param name="cancellationToken">Stops reading.</param>
    /// <returns>The messages.</returns>
    IAsyncEnumerable<InstanceMessage> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Single instance: a <see cref="InstanceNames.Mutex"/> mutex plus a per-user named pipe. Implemented by platform.
/// </summary>
/// <remarks>
/// A normal launch calls <see cref="TryClaim"/>; the first instance <see cref="Listen"/>s, later ones
/// <see cref="ConnectAsync"/>, send their command, wait for the acknowledgement and exit. The screen saver process only
/// connects (never claims).
/// </remarks>
public interface ISingleInstance : IDisposable
{
    /// <summary>Takes the mutex.</summary>
    /// <returns>True when this process is the first instance.</returns>
    bool TryClaim();

    /// <summary>Starts accepting connections (first instance only); each connection is handled by the callback on the thread pool.</summary>
    /// <param name="onConnection">Handles one connection; the connection closes when the returned task completes.</param>
    void Listen(Func<IInstanceConnection, Task> onConnection);

    /// <summary>Connects to the running instance.</summary>
    /// <param name="timeout">How long to wait for the pipe.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The connection, or null when no instance answers.</returns>
    Task<IInstanceConnection?> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
