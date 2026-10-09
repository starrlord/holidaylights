using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Integration;

/// <summary>
/// Recognizes the Holiday Lights 5.4 screen saver (PRODUCT-SPEC 3.5.2): string resource 2 of the file is
/// "TigerTechHolidayLights" (what 5.4 itself tests), or the file is missing and its name is
/// <c>HOLIDA~1.SCR</c> or <c>Holiday Lights.scr</c> (the 32-bit installer wrote <c>system32\HOLIDA~1.SCR</c> while WOW64
/// put the file in SysWOW64, so 64-bit Windows can never start it).
/// </summary>
internal static unsafe class LegacyScreenSaver
{
    /// <summary>String resource 2 of every 5.4 <c>.scr</c>.</summary>
    internal const string Signature = "TigerTechHolidayLights";

    /// <summary>True when the path is the (usually broken) 5.4 screen saver.</summary>
    /// <param name="path">The <c>SCRNSAVE.EXE</c> value.</param>
    /// <returns>True for a 5.4 saver.</returns>
    public static bool IsHolidayLights54(string? path)
    {
        string? file = PathText.Normalize(path);
        if (file is null)
        {
            return false;
        }

        if (File.Exists(file))
        {
            return string.Equals(ReadStringResource(file, 2), Signature, StringComparison.Ordinal);
        }

        string name = Path.GetFileName(file);
        return name.Equals("HOLIDA~1.SCR", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Holiday Lights.scr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a string resource without running the file (loaded as an image resource, 32- or 64-bit).</summary>
    /// <param name="path">The executable.</param>
    /// <param name="id">The string id.</param>
    /// <returns>The string, or null when the file or the string cannot be read.</returns>
    internal static string? ReadStringResource(string path, uint id)
    {
        nint module = NativeMethods.LoadLibraryEx(path, 0, NativeMethods.LOAD_LIBRARY_AS_DATAFILE | NativeMethods.LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (module == 0)
        {
            return null;
        }

        try
        {
            char* buffer = stackalloc char[256];
            int length = NativeMethods.LoadString(module, id, buffer, 256);
            return length > 0 ? new string(buffer, 0, length) : null;
        }
        finally
        {
            NativeMethods.FreeLibrary(module);
        }
    }
}
