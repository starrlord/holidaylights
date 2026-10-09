using System.Security;

namespace HolidayLights.Platform.Integration;

/// <summary>
/// The guard every write to Windows goes through: with <c>--no-system-changes</c> the write is skipped and logged
/// (PRODUCT-SPEC PO-3), and a failing write is logged instead of thrown (the UI shows the state it reads back).
/// </summary>
internal sealed class SystemChanges
{
    private readonly AppRuntimeOptions options;
    private readonly IAppLog log;
    private readonly string source;

    /// <summary>Creates the guard.</summary>
    /// <param name="options">Whether system changes are allowed.</param>
    /// <param name="log">The log.</param>
    /// <param name="source">Log source of the owning service.</param>
    public SystemChanges(AppRuntimeOptions options, IAppLog log, string source)
    {
        this.options = options;
        this.log = log;
        this.source = source;
    }

    /// <summary>Runs a change unless system changes are off.</summary>
    /// <param name="description">What the change does, for the log ("write the Run value").</param>
    /// <param name="change">The change.</param>
    /// <returns>True when it ran and succeeded.</returns>
    public bool Apply(string description, Action change)
    {
        if (!options.AllowSystemChanges)
        {
            log.Info(source, $"System changes are turned off: skipped \"{description}\".");
            return false;
        }

        try
        {
            change();
            log.Info(source, $"Done: {description}.");
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            log.Error(source, $"Could not {description}.", e);
            return false;
        }
    }
}
