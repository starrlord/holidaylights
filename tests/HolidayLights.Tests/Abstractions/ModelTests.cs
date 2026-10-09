namespace HolidayLights.Tests.Abstractions;

public sealed class ModelTests
{
    [Theory]
    [InlineData(32, 1.5, 48)]
    [InlineData(32, 1.0, 32)]
    [InlineData(13, 1.5, 20)]
    [InlineData(7, 1.5, 11)]
    [InlineData(2, 0.75, 2)]
    [InlineData(1, 0.25, 1)]
    [InlineData(0, 1.5, 0)]
    [InlineData(8, 2.25, 18)]
    public void ArtScale_RoundsHalfAwayFromZeroAndKeepsPositiveLengths(int length, double scale, int expected) =>
        Assert.Equal(expected, ArtScale.Scale(length, scale));

    [Fact]
    public void ArtScale_EffectiveScaleOfTheReferencePc() =>
        Assert.Equal(1.5, ArtScale.Effective(144 / 96.0, BulbSize.Standard));

    [Fact]
    public void CellSlots_ConvertSidesAndCorners()
    {
        Assert.Equal(CellSlot.Left, Side.Left.ToSlot());
        Assert.Equal(CellSlot.BottomRight, Corner.BottomRight.ToSlot());
        Assert.Equal(Corner.BottomLeft, CellSlot.BottomLeft.ToCorner());
        Assert.Equal(Side.Right, CellSlot.Right.ToSide());
        Assert.True(CellSlot.TopLeft.IsCorner());
        Assert.False(CellSlot.Preview.IsSide());
        Assert.Throws<ArgumentOutOfRangeException>(() => CellSlot.Preview.ToSide());
        Assert.Equal((Corner.BottomLeft, Corner.BottomRight), CellSlots.CornersOf(Side.Bottom));
        Assert.Equal([Side.Top, Side.Bottom, Side.Right, Side.Left], CellSlots.BuildOrder);
    }

    [Fact]
    public void BulbIds_ParseOrigins()
    {
        Assert.True(BulbIds.TryGetOrigin("builtin:standard-bulbs", out BulbOrigin builtIn));
        Assert.Equal(BulbOrigin.BuiltIn, builtIn);
        Assert.True(BulbIds.TryGetOrigin("ADDON:Arrow", out BulbOrigin addOn));
        Assert.Equal(BulbOrigin.BundledAddOn, addOn);
        Assert.Equal("Star", BulbIds.GetKey(BulbIds.User("Star")));
        Assert.False(BulbIds.IsValid("addon:"));
        Assert.False(BulbIds.IsValid("standard-bulbs"));
        Assert.True(BulbIds.Comparer.Equals("addon:Arrow", "addon:arrow"));
    }

    [Fact]
    public void MediaIds_SplitOriginAndFileName()
    {
        Assert.True(MediaIds.TryParse("bundled:Santa Candle.BMP", out MediaOrigin origin, out string fileName));
        Assert.Equal(MediaOrigin.Bundled, origin);
        Assert.Equal("Santa Candle.BMP", fileName);
        Assert.False(MediaIds.TryParse("Santa Candle.BMP", out _, out _));
    }

    [Fact]
    public void SlotAssignment_ComparesBoxByBox()
    {
        SlotAssignment copy = SlotAssignment.Empty
            .WithEdge(Side.Top, ["builtin:standard-bulbs"])
            .WithEdge(Side.Right, ["builtin:standard-bulbs", "builtin:snow-family"])
            .WithEdge(Side.Bottom, ["builtin:snow-family", "builtin:jolly-holly"])
            .WithEdge(Side.Left, ["BUILTIN:standard-bulbs", "builtin:snow-family"])
            .WithCorner(Corner.TopLeft, "builtin:jolly-holly")
            .WithCorner(Corner.TopRight, "builtin:jolly-holly")
            .WithCorner(Corner.BottomRight, "builtin:jolly-holly")
            .WithCorner(Corner.BottomLeft, "builtin:jolly-holly");

        Assert.Equal(SlotAssignment.Classic54Default, copy);
        Assert.Equal(SlotAssignment.Classic54Default.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(SlotAssignment.Classic54Default, copy.WithCorner(Corner.TopLeft, null));
        Assert.True(SlotAssignment.Empty.HasNoBulbs());
        Assert.True(copy.Contains("builtin:snow-family"));
        Assert.Throws<ArgumentException>(() => copy.WithEdge(Side.Top, Enumerable.Repeat("builtin:sun", 7)));
    }

    [Fact]
    public void LookPresets_DetectThePresetsAndCustom()
    {
        Assert.Equal(LookPreset.ModernGlow, LookPresets.Detect(new LookSettings()));
        Assert.Equal(LookPreset.Classic2003, LookPresets.Detect(LookPresets.ValuesOf(LookPreset.Classic2003)!));
        Assert.Equal(LookPreset.Custom, LookPresets.Detect(new LookSettings { Glow = GlowLevel.Off }));
        Assert.Null(LookPresets.ValuesOf(LookPreset.Custom));
    }

    [Fact]
    public void FlashSettings_MapsTheSliderLike54()
    {
        Assert.Equal(9, FlashSettings.IntervalFromSliderPosition(1));
        Assert.Equal(5, FlashSettings.IntervalFromSliderPosition(5));
        Assert.Equal(1, FlashSettings.IntervalFromSliderPosition(9));
        Assert.Equal(8, FlashSettings.SliderPositionFromInterval(2));
    }

    [Theory]
    [InlineData("Leaves", SaverMovementStyle.FallingLeaves)]
    [InlineData("Easter Eggs", SaverMovementStyle.GravityWell)]
    [InlineData("Happy Faces", SaverMovementStyle.GravityWell)]
    [InlineData("Halloween", SaverMovementStyle.Attraction)]
    [InlineData("Heavens Above", SaverMovementStyle.Attraction)]
    [InlineData("Snow", SaverMovementStyle.BounceOffSides)]
    [InlineData("Dreidels", SaverMovementStyle.BounceOffSides)]
    public void SaverAnimations_DeriveThe54Style(string animation, SaverMovementStyle expected) =>
        Assert.Equal(expected, SaverAnimations.DefaultStyleFor(animation));

    [Fact]
    public void SaverAnimations_ListThe25ChoicesInListOrder()
    {
        Assert.Equal(25, SaverAnimations.All.Count);
        Assert.Equal("(None)", SaverAnimations.All[0]);
        Assert.True(SaverAnimations.HasOwnMovement("Balloons"));
        Assert.True(SaverAnimations.TryGetBulbId(SaverAnimations.ForBulb("addon:Arrow"), out string? bulbId));
        Assert.Equal("addon:Arrow", bulbId);
    }

    [Fact]
    public void ShippedThemeNames_Are19()
    {
        Assert.Equal(11, ShippedThemeNames.Classic.Count);
        Assert.Equal(8, ShippedThemeNames.New.Count);
        Assert.Equal(19, ShippedThemeNames.Classic.Concat(ShippedThemeNames.New).Distinct().Count());
    }

    [Fact]
    public void CalendarDefaults_CoverEveryEntryOfTheSpec()
    {
        IReadOnlyList<CalendarEntry> entries = CalendarSettings.UnitedStatesDefaults;

        Assert.Equal(
            ["newYear", "valentinesDay", "stPatricksDay", "easter", "july4th", "halloween", "thanksgiving", "christmas", "chanukah", "winter", "spring", "summer", "autumn"],
            entries.Select(e => e.Id));
        Assert.Equal(8, entries.Count(e => e.Use));
        Assert.Equal(ShippedThemeNames.Christmas1, entries.Single(e => e.Id == "christmas").Theme);
    }

    [Fact]
    public void HotKeyBinding_FormatsKeyCaps()
    {
        var binding = new HotKeyBinding { Enabled = true, Key = "B" };

        Assert.Equal("Ctrl + Alt + Shift + B", binding.Format());
        Assert.Equal("Ctrl+Alt+Shift+B", binding.Format("+"));
    }

    [Fact]
    public void LayerModes_FollowBulbDrawing()
    {
        Assert.Equal(LayerMode.BehindIcons, LayerModes.FromSettings(BulbDrawing.Desktop, behindIcons: true));
        Assert.Equal(LayerMode.InFrontOfIcons, LayerModes.FromSettings(BulbDrawing.Desktop, behindIcons: false));
        Assert.Equal(LayerMode.OnTop, LayerModes.FromSettings(BulbDrawing.OnTop, behindIcons: true));
    }

    [Fact]
    public void DanceEnvelope_AttacksThenDecays()
    {
        Assert.Equal(0.2, DanceEnvelope.BrightnessAt(0.2, 1.0, 0), 6);
        Assert.Equal(0.6, DanceEnvelope.BrightnessAt(0.2, 1.0, 15), 6);
        Assert.Equal(1.0, DanceEnvelope.BrightnessAt(0.2, 1.0, 30), 6);
        Assert.Equal(Math.Exp(-1), DanceEnvelope.BrightnessAt(0.2, 1.0, 280), 6);
    }

    [Fact]
    public void BrightnessWave_BreathesOverSixteenSteps()
    {
        var wave = new BrightnessWave(16, 0);

        Assert.Equal(0.0, wave.Evaluate(0), 9);
        Assert.Equal(1.0, wave.Evaluate(8), 9);
        Assert.Equal(0.5, new BrightnessWave(16, 0.25).Evaluate(8), 9);
    }

    [Fact]
    public void SaverFontSubstitutes_UseTheFirstInstalledLookAlike()
    {
        string[] installed = ["Arial", "Ink Free", "Georgia", "Segoe Script"];
        bool IsInstalled(string family) => installed.Contains(family, StringComparer.OrdinalIgnoreCase);
        var creepy = new SaverFont { Family = "Creepy", SizePt = 60, Bold = true, Italic = true };

        Assert.Equal(creepy with { Family = "Ink Free" }, SaverFontSubstitutes.Resolve(creepy, IsInstalled));
        Assert.Equal("Georgia", SaverFontSubstitutes.Resolve(new SaverFont { Family = "Figaro MT" }, IsInstalled).Family);
        Assert.Equal(new SaverFont { Family = "Arial", SizePt = 36, Bold = true }, SaverFontSubstitutes.Resolve(new SaverFont { Family = "Wingdings 9", SizePt = 72 }, IsInstalled));
        Assert.Same(creepy, SaverFontSubstitutes.Resolve(creepy, _ => true));
        Assert.Equal(["Chiller", "Ink Free"], SaverFontSubstitutes.LookAlikesOf("creepy"));
    }
}
