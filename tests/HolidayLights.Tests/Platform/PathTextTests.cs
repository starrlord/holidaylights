using HolidayLights.Platform.Integration;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

public sealed class PathTextTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Holiday Lights\\HolidayLights.exe\" --autostart", "C:\\Program Files\\Holiday Lights\\HolidayLights.exe")]
    [InlineData("\"C:\\x\\HolidayLights.exe\" --open \"%1\"", "C:\\x\\HolidayLights.exe")]
    [InlineData("  \"C:\\x\\a.exe\"", "C:\\x\\a.exe")]
    [InlineData("\"C:\\unterminated\\a.exe", "C:\\unterminated\\a.exe")]
    [InlineData("C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe /background", "C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe")]
    [InlineData("C:\\PROGRA~1\\HOLIDA~1\\HOLIDA~1.EXE open %1", "C:\\PROGRA~1\\HOLIDA~1\\HOLIDA~1.EXE")]
    [InlineData("C:\\WINDOWS\\system32\\HOLIDA~1.SCR", "C:\\WINDOWS\\system32\\HOLIDA~1.SCR")]
    [InlineData("C:\\a.executable\\b.exe -x", "C:\\a.executable\\b.exe")]
    [InlineData("C:\\tools\\run -x", "C:\\tools\\run")]
    [InlineData("notepad", "notepad")]
    public void ProgramOf_FindsTheProgramOfACommandLine(string commandLine, string expected) =>
        Assert.Equal(expected, PathText.ProgramOf(commandLine));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void ProgramOf_EmptyCommandsHaveNoProgram(string? commandLine) => Assert.Null(PathText.ProgramOf(commandLine));

    [Fact]
    public void SameFile_IgnoresCaseQuotesAndEnvironmentVariables()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.True(PathText.SameFile("\"%SystemRoot%\\System32\\NOTEPAD.EXE\"", Path.Combine(windows, "system32", "notepad.exe")));
        Assert.True(PathText.SameFile(@"C:\Temp\..\Temp\x.scr", @"c:\temp\X.SCR"));
        Assert.False(PathText.SameFile(@"C:\a\x.scr", @"C:\b\x.scr"));
        Assert.False(PathText.SameFile(null, @"C:\a"));
        Assert.False(PathText.SameFile("", ""));
    }

    [Fact]
    public void SameFile_MatchesAShortNameWithItsLongName()
    {
        var root = new TempDataRoot();
        try
        {
            string file = Path.Combine(root.Root, "Holiday Lights Screen Saver.scr");
            File.WriteAllText(file, "x");
            string shortName = ShortName(file);
            if (string.Equals(shortName, file, StringComparison.OrdinalIgnoreCase))
            {
                return; // 8.3 names are turned off on this volume.
            }

            Assert.True(PathText.SameFile(shortName, file));
            Assert.Equal(file, PathText.LongName(shortName), ignoreCase: true);
        }
        finally
        {
            TestFolders.Delete(root);
        }
    }

    private static unsafe string ShortName(string path)
    {
        char* buffer = stackalloc char[1024];
        uint length = GetShortPathName(path, buffer, 1024);
        return length > 0 ? new string(buffer, 0, (int)length) : path;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern unsafe uint GetShortPathName(string longPath, char* shortPath, uint bufferLength);
}
