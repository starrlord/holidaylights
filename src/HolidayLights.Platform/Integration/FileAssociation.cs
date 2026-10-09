using HolidayLights.Platform.Native;
using Microsoft.Win32;

namespace HolidayLights.Platform.Integration;

/// <summary>The per-user <c>.bul</c> association (see <see cref="IFileAssociation"/>). Owner: platform.</summary>
/// <remarks>
/// <para>Written under <c>HKCU\Software\Classes</c> (no elevation, PRODUCT-SPEC 6.9): <c>.bul</c> (default) =
/// <c>TigerTech.HolidayLights.Bulb</c> plus an <c>OpenWithProgids</c> entry; the ProgID "Holiday Lights Bulb" with the
/// document icon and <c>open</c> = <c>"&lt;install&gt;\HolidayLights.exe" --open "%1"</c>; then
/// <c>SHChangeNotify(SHCNE_ASSOCCHANGED)</c>.</para>
/// <para>Another program is never displaced: a <c>UserChoice</c> for <c>.bul</c> (the user picked a program in "Open
/// with") or a per-user default naming another ProgID makes the state <see cref="FileAssociationState.OwnedByAnotherProgram"/>,
/// and <see cref="Register"/> then only adds Holiday Lights to "Open with". The machine-wide 5.4 registration
/// (<c>HolidayLights.Bulb</c>) is replaced, since 6.0 takes over from 5.4.</para>
/// </remarks>
public sealed class FileAssociation : IFileAssociation
{
    /// <summary>The ProgID.</summary>
    internal const string ProgId = "TigerTech.HolidayLights.Bulb";

    /// <summary>The ProgID of Holiday Lights 5.4 (machine-wide; replaced, never treated as another program).</summary>
    internal const string LegacyProgId = "HolidayLights.Bulb";

    /// <summary>The extension key (relative to HKCU).</summary>
    internal const string ExtensionKey = @"Software\Classes\.bul";

    /// <summary>The ProgID key (relative to HKCU).</summary>
    internal const string ProgIdKey = @"Software\Classes\" + ProgId;

    /// <summary>The open command key (relative to HKCU).</summary>
    internal const string CommandKey = ProgIdKey + @"\shell\open\command";

    /// <summary>Where Explorer keeps the user's "Open with" choice (relative to HKCU).</summary>
    internal const string UserChoiceKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.bul\UserChoice";

    private const string OpenWithKey = ExtensionKey + @"\OpenWithProgids";
    private const string LogSource = "Platform.FileAssociation";

    private readonly DataPaths paths;
    private readonly IAppLog log;
    private readonly UserRegistry registry;
    private readonly SystemChanges changes;
    private readonly Action notifyShell;

    /// <summary>Creates the service.</summary>
    /// <param name="paths">The program and document icon paths.</param>
    /// <param name="options">With <see cref="AppRuntimeOptions.AllowSystemChanges"/> false, writes are skipped and logged.</param>
    /// <param name="log">The log.</param>
    public FileAssociation(DataPaths paths, AppRuntimeOptions options, IAppLog log)
        : this(paths, options, log, UserRegistry.CurrentUser, NotifyAssociationsChanged)
    {
    }

    /// <summary>Creates the service on another registry root (tests).</summary>
    /// <param name="paths">The program and document icon paths.</param>
    /// <param name="options">Whether writes are allowed.</param>
    /// <param name="log">The log.</param>
    /// <param name="registry">The key that stands for HKCU.</param>
    /// <param name="notifyShell">Tells the shell that associations changed.</param>
    internal FileAssociation(DataPaths paths, AppRuntimeOptions options, IAppLog log, UserRegistry registry, Action notifyShell)
    {
        this.paths = paths;
        this.log = log;
        this.registry = registry;
        this.notifyShell = notifyShell;
        changes = new SystemChanges(options, log, LogSource);
    }

    /// <summary>The open command this installation writes.</summary>
    internal string OpenCommand => $"\"{paths.InstalledExePath}\" --open \"%1\"";

    /// <inheritdoc />
    public FileAssociationState GetState()
    {
        string? userChoice = registry.ReadString(UserChoiceKey, "ProgId");
        string? extensionDefault = registry.ReadString(ExtensionKey, "");
        if (!string.IsNullOrEmpty(userChoice) && !IsOurs(userChoice))
        {
            return FileAssociationState.OwnedByAnotherProgram;
        }

        if (string.IsNullOrEmpty(userChoice) && IsAnotherProgram(extensionDefault))
        {
            return FileAssociationState.OwnedByAnotherProgram;
        }

        if (!IsOurs(userChoice) && !IsOurs(extensionDefault))
        {
            return FileAssociationState.NotRegistered;
        }

        string? program = PathText.ProgramOf(registry.ReadString(CommandKey, ""));
        return program is not null && File.Exists(Environment.ExpandEnvironmentVariables(program))
            ? FileAssociationState.Registered
            : FileAssociationState.Broken;
    }

    /// <inheritdoc />
    public void Register()
    {
        bool takeDefault = !IsAnotherProgram(registry.ReadString(ExtensionKey, ""));
        bool written = changes.Apply("register .bul files", () =>
        {
            registry.Write(ProgIdKey, "", "Holiday Lights Bulb");
            registry.Write(ProgIdKey + @"\DefaultIcon", "", paths.BulbDocumentIconPath);
            registry.Write(CommandKey, "", OpenCommand);
            registry.Write(OpenWithKey, ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            if (takeDefault)
            {
                registry.Write(ExtensionKey, "", ProgId);
            }
        });

        if (written)
        {
            if (!takeDefault)
            {
                log.Info(LogSource, "Another program opens .bul files; Holiday Lights was only added to \"Open with\".");
            }

            notifyShell();
        }
    }

    /// <inheritdoc />
    public void Unregister()
    {
        bool written = changes.Apply("remove the .bul registration", () =>
        {
            registry.DeleteTree(ProgIdKey);
            registry.DeleteValue(OpenWithKey, ProgId);
            registry.DeleteIfEmpty(OpenWithKey);
            if (IsOurs(registry.ReadString(ExtensionKey, "")))
            {
                registry.DeleteValue(ExtensionKey, "");
            }

            registry.DeleteIfEmpty(ExtensionKey);
        });

        if (written)
        {
            notifyShell();
        }
    }

    private static bool IsOurs(string? progId) => string.Equals(progId, ProgId, StringComparison.OrdinalIgnoreCase);

    /// <summary>A per-user default that names a ProgID other than ours or the 5.4 one.</summary>
    private static bool IsAnotherProgram(string? progId) =>
        !string.IsNullOrEmpty(progId) && !IsOurs(progId) && !string.Equals(progId, LegacyProgId, StringComparison.OrdinalIgnoreCase);

    private static void NotifyAssociationsChanged() =>
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, 0, 0);
}
