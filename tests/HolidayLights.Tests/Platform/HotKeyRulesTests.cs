using HolidayLights.Platform.Input;

namespace HolidayLights.Tests.Platform;

public sealed class HotKeyRulesTests
{
    private const HotKeyModifiers CtrlAltShift = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift;

    [Theory]
    [InlineData("A", 0x41u)]
    [InlineData("z", 0x5Au)]
    [InlineData("0", 0x30u)]
    [InlineData("9", 0x39u)]
    [InlineData("NumPad0", 0x60u)]
    [InlineData("numpad9", 0x69u)]
    [InlineData("F1", 0x70u)]
    [InlineData("F12", 0x7Bu)]
    [InlineData("F24", 0x87u)]
    [InlineData("Insert", 0x2Du)]
    [InlineData("Home", 0x24u)]
    [InlineData("End", 0x23u)]
    [InlineData("PageUp", 0x21u)]
    [InlineData("PageDown", 0x22u)]
    [InlineData("Pause", 0x13u)]
    public void Keys_MapToTheirVirtualKeyCodes(string key, uint expected)
    {
        Assert.True(HotKeyKeys.TryGetVirtualKey(key, out uint virtualKey));
        Assert.Equal(expected, virtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Escape")]
    [InlineData("Delete")]
    [InlineData("Tab")]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("F01")]
    [InlineData("NumPad10")]
    [InlineData("NumPad")]
    [InlineData("+")]
    [InlineData("é")]
    public void Keys_OutsideTheAcceptedSetAreRefused(string? key)
    {
        Assert.False(HotKeyKeys.TryGetVirtualKey(key, out _));
        Assert.Equal(HotKeyValidity.KeyNotAllowed, Check(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, key ?? "").Validity);
    }

    [Fact]
    public void ShiftAndALetter_WouldStopCapitalLetters()
    {
        HotKeyCheck check = Check(HotKeyModifiers.Shift, "B");

        Assert.Equal(HotKeyValidity.ShiftLetterOnly, check.Validity);
        Assert.False(check.CanSave);
    }

    [Theory]
    [InlineData(HotKeyModifiers.None, "B")]
    [InlineData(HotKeyModifiers.None, "7")]
    [InlineData(HotKeyModifiers.Shift, "7")]
    [InlineData(HotKeyModifiers.Shift, "Home")]
    public void CombinationsWithoutCtrlAltOrWindows_NeedAModifier(HotKeyModifiers modifiers, string key) =>
        Assert.Equal(HotKeyValidity.NeedsModifier, Check(modifiers, key).Validity);

    [Theory]
    [InlineData(HotKeyModifiers.None, "F7")]
    [InlineData(HotKeyModifiers.Shift, "F24")]
    [InlineData(HotKeyModifiers.None, "Pause")]
    public void FunctionKeysAndPause_WorkWithoutAModifier(HotKeyModifiers modifiers, string key) =>
        Assert.Equal(HotKeyValidity.Ok, Check(modifiers, key).Validity);

    [Fact]
    public void TheDefaultCombination_IsFine()
    {
        HotKeyCheck check = Check(CtrlAltShift, "B");

        Assert.Equal(HotKeyValidity.Ok, check.Validity);
        Assert.True(check.CanSave);
    }

    [Fact]
    public void ACombinationAnotherProgramOwns_IsInUse()
    {
        uint? asked = null;
        HotKeyCheck check = HotKeyRules.Validate(
            new HotKeyBinding { Modifiers = CtrlAltShift, Key = "L" }, vk => (asked = vk) == 0x4C, (_, _) => null);

        Assert.Equal(0x4Cu, asked);
        Assert.Equal(HotKeyValidity.InUse, check.Validity);
        Assert.False(check.CanSave);
    }

    [Fact]
    public void InUse_IsReportedBeforeTheAltGrCheck()
    {
        HotKeyCheck check = HotKeyRules.Validate(
            new HotKeyBinding { Modifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, Key = "L" },
            _ => true,
            (_, _) => new AltGrCharacter("Ł", "Polish (Programmers)"));

        Assert.Equal(HotKeyValidity.InUse, check.Validity);
    }

    [Fact]
    public void ACharacterTypedWithAltGr_IsRefusedWithTheCharacterAndLayout()
    {
        HotKeyModifiers? askedModifiers = null;
        HotKeyCheck check = HotKeyRules.Validate(
            new HotKeyBinding { Modifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, Key = "L" },
            _ => false,
            (vk, modifiers) =>
            {
                askedModifiers = modifiers;
                return vk == 0x4C ? new AltGrCharacter("Ł", "Polish (Programmers)") : null;
            });

        Assert.Equal(HotKeyValidity.TypesCharacter, check.Validity);
        Assert.Equal("Ł", check.TypedCharacter);
        Assert.Equal("Polish (Programmers)", check.LayoutName);
        Assert.Equal(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, askedModifiers);
        Assert.False(check.CanSave);
    }

    [Theory]
    [InlineData(HotKeyModifiers.Ctrl | HotKeyModifiers.Shift)]
    [InlineData(HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    [InlineData(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Win)]
    public void TheAltGrCheck_OnlyAppliesToCtrlAltWithoutWindows(HotKeyModifiers modifiers)
    {
        bool asked = false;
        HotKeyRules.Validate(new HotKeyBinding { Modifiers = modifiers, Key = "Q" }, _ => false, (_, _) =>
        {
            asked = true;
            return new AltGrCharacter("@", "German");
        });

        Assert.False(asked);
    }

    [Theory]
    [InlineData("B")]
    [InlineData("T")]
    [InlineData("n")]
    public void BrowserShortcuts_WarnButCanBeSaved(string key)
    {
        HotKeyCheck check = Check(HotKeyModifiers.Ctrl | HotKeyModifiers.Shift, key);

        Assert.Equal(HotKeyValidity.BrowserShortcut, check.Validity);
        Assert.True(check.CanSave);
    }

    [Fact]
    public void OnlyCtrlShiftWithABrowserKey_Warns()
    {
        Assert.Equal(HotKeyValidity.Ok, Check(CtrlAltShift, "T").Validity);
        Assert.Equal(HotKeyValidity.Ok, Check(HotKeyModifiers.Ctrl | HotKeyModifiers.Shift, "Q").Validity);
    }

    [Fact]
    public void WindowsKeyCombinations_WarnButCanBeSaved()
    {
        HotKeyCheck check = Check(HotKeyModifiers.Win | HotKeyModifiers.Shift, "H");

        Assert.Equal(HotKeyValidity.WindowsKeyCombination, check.Validity);
        Assert.True(check.CanSave);
    }

    [Fact]
    public void CheckShape_ReportsTheVirtualKey()
    {
        Assert.Null(HotKeyRules.CheckShape(new HotKeyBinding { Modifiers = CtrlAltShift, Key = "F9" }, out uint virtualKey));
        Assert.Equal(0x78u, virtualKey);
    }

    private static HotKeyCheck Check(HotKeyModifiers modifiers, string key) =>
        HotKeyRules.Validate(new HotKeyBinding { Modifiers = modifiers, Key = key }, _ => false, (_, _) => null);
}
