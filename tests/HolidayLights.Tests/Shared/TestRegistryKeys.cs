using Microsoft.Win32;

namespace HolidayLights.Tests.Shared;

/// <summary>
/// Private registry keys for tests: <c>HKCU\Software\HolidayLightsTests\&lt;guid&gt;</c>. Each test deletes only its own
/// key. The shared parent is removed once, when the test process exits, so one test class never deletes it while another
/// (running in parallel) creates its key inside it.
/// </summary>
internal static class TestRegistryKeys
{
    private const string Parent = @"Software\HolidayLightsTests";

    static TestRegistryKeys() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteParentIfEmpty();

    /// <summary>A new, unique key path below the shared parent (not created yet).</summary>
    public static string NewPath() => $@"{Parent}\{Guid.NewGuid():N}";

    /// <summary>Deletes a test's key and everything below it.</summary>
    public static void Delete(string path) => Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);

    private static void DeleteParentIfEmpty()
    {
        try
        {
            using (RegistryKey? parent = Registry.CurrentUser.OpenSubKey(Parent))
            {
                if (parent is not { SubKeyCount: 0, ValueCount: 0 })
                {
                    return;
                }
            }

            Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false);
        }
        catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            // Another test process is still using it; the last one to exit removes it.
        }
    }
}
