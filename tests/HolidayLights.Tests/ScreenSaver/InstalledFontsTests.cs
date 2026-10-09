using HolidayLights.App.Controls;
using HolidayLights.App.ScreenSaver.Rendering;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>Review r1 #37: GDI faces that WPF files under another family (Arial Black, Footlight MT Light) are installed fonts.</summary>
public sealed class InstalledFontsTests
{
    private static readonly string FontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    [Fact]
    public void AFamilyWpfListsIsInstalled_AndAMissingOneIsNot()
    {
        Assert.True(InstalledFonts.IsInstalled("Arial"));
        Assert.True(InstalledFonts.IsInstalled("arial"));
        Assert.False(InstalledFonts.IsInstalled("No Such Font Holiday Lights 6"));
        Assert.False(InstalledFonts.IsInstalled(""));
        Assert.Contains("Arial", InstalledFonts.All);
        Assert.DoesNotContain(InstalledFonts.All, n => n.StartsWith('@'));
    }

    [Fact]
    public void ArialBlack_IsInstalled_ListedInThePicker_AndKeptByTheSaver()
    {
        if (!File.Exists(Path.Combine(FontsFolder, "ARIBLK.TTF")))
        {
            return; // Arial Black ships with Windows; nothing to check without it.
        }

        Assert.True(SaverText.IsInstalled("Arial Black"));
        Assert.True(FontPicker.IsInstalled("Arial Black"));
        Assert.Contains("Arial Black", InstalledFonts.All, StringComparer.OrdinalIgnoreCase);

        // July 4th's font: drawn as itself at its own size, not Arial Bold 36 pt.
        var july4 = new SaverFont { Family = "Arial Black", SizePt = 48, Bold = true };
        Assert.Equal(july4, SaverText.ResolveFont(july4));
        Assert.Null(FontPicker.ChoiceFor("Arial Black", FontPicker.IsInstalled).Note);
    }

    [Fact]
    public void FootlightMtLight_IsInstalledWhenItsFileIs()
    {
        if (!File.Exists(Path.Combine(FontsFolder, "FTLTLT.TTF")))
        {
            return; // Footlight MT Light comes with Office.
        }

        Assert.True(SaverText.IsInstalled("Footlight MT Light"));
    }
}
