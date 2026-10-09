using System.IO;

namespace HolidayLights.App.Settings;

/// <summary>
/// The file problems the Settings window reports instead of failing (PRODUCT-SPEC 5.13: "Disk full or file write
/// failure: InfoBar on the page that wrote"; Appendix D: "Couldn't ...: &lt;Windows reason&gt;").
/// </summary>
public static class FileProblems
{
    /// <summary>
    /// True for the exceptions a theme, bulb, song or picture operation throws for a problem the user can fix or that
    /// another program caused: a locked, read-only or missing file, a full disk, missing permissions, or a name that was
    /// taken or removed meanwhile.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True when it should become a message.</returns>
    public static bool Is(Exception exception) => exception switch
    {
        IOException or UnauthorizedAccessException or System.Security.SecurityException => true,
        ArgumentNullException or ArgumentOutOfRangeException => false,
        ArgumentException => true,
        _ => false,
    };

    /// <summary>"Couldn't delete Halloween: The process cannot access the file because it is being used by another process."</summary>
    /// <param name="what">What failed, without the reason ("Couldn't delete Halloween").</param>
    /// <param name="exception">The exception.</param>
    /// <returns>The sentence.</returns>
    public static string Sentence(string what, Exception exception)
    {
        ArgumentException.ThrowIfNullOrEmpty(what);
        ArgumentNullException.ThrowIfNull(exception);
        string reason = exception.Message.Trim();
        if (reason.Length == 0)
        {
            return what + ".";
        }

        return reason.EndsWith('.') || reason.EndsWith('!') || reason.EndsWith('?') ? $"{what}: {reason}" : $"{what}: {reason}.";
    }
}
