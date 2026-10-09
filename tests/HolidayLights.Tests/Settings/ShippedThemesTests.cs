using System.Text.Json;
using HolidayLights.Core.Legacy;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

/// <summary>The 19 shipped themes of PRODUCT-SPEC 7.2, including its build-time check of bulbs, songs, pictures and fonts.</summary>
public sealed class ShippedThemesTests
{
    /// <summary>Fonts that ship with Windows 11 (the fonts the new themes may use).</summary>
    private static readonly HashSet<string> Windows11Fonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Arial", "Arial Black", "Comic Sans MS", "Georgia", "Impact", "Segoe Script", "Segoe UI", "Times New Roman", "Verdana", "Ink Free",
    };

    [Fact]
    public void TheNineteenThemesAreEmbedded_ClassicThenNew()
    {
        Assert.Equal(19, EmbeddedAssets.List("themes/").Count(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal([.. ShippedThemeNames.Classic, .. ShippedThemeNames.New], ShippedThemes.All.Select(t => t.Name));
        Assert.All(ShippedThemes.All.Take(11), t => Assert.Equal(ShippedThemeKind.Classic, t.Shipped));
        Assert.All(ShippedThemes.All.Skip(11), t => Assert.Equal(ShippedThemeKind.New, t.Shipped));
    }

    [Fact]
    public void ClassicThemes_AreTheInstallerThemesExactlyAsStored()
    {
        var mapper = new LegacySettingsMapper(LegacyResolver.ForComparison);
        IReadOnlyList<KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>> installer = GoldenRegistry.InstallerThemes();

        Assert.Equal(ShippedThemeNames.Classic.Order(), installer.Select(t => t.Key).Order());
        foreach ((string name, IReadOnlyDictionary<string, LegacyValue> values) in installer)
        {
            var issues = new LegacyMappingIssues();
            ThemeDefinition mapped = ThemeNormalizer.Normalize(mapper.MapTheme(name, new LegacyValueSet(values), issues), name);
            ThemeDefinition shipped = ShippedThemes.Find(name)!;

            Assert.Empty(issues.UnresolvedBulbs);
            Assert.True(ThemeValues.Equal(shipped, mapped), $"{name} differs from the installer values.");
            Assert.Equal(mapped.Music!.EnabledSongs, shipped.Music!.EnabledSongs);
            Assert.Null(shipped.Saver!.Background);
            Assert.Null(shipped.Saver.Style);
        }
    }

    [Theory]
    [InlineData("Blank Slate", "flashTogether", 5, 0, "always", "(None)", "(None)", "", "Arial", 36, true)]
    [InlineData("Chanukah", "bulbChase", 5, 3, "intermittently", "Dreidels", "(None)", "Happy Chanukah!", "Footlight MT Light", 48, true)]
    [InlineData("Christmas 1", "flashTogether", 5, 31, "always", "Snow", "bundled:Santa Candle.BMP", "Merry Christmas!", "Lucida Handwriting", 48, true)]
    [InlineData("Christmas 2", "flashTogether", 5, 31, "always", "Santa", "bundled:Snowman.BMP", "Merry Christmas!", "Lucida Handwriting", 48, true)]
    [InlineData("Easter Eggs", "alternating", 7, 0, "always", "Easter Eggs", "bundled:Easter Bunny.BMP", "Happy Easter!", "Comic Sans MS", 42, true)]
    [InlineData("Halloween", "flashTogether", 5, 5, "intermittently", "Halloween", "bundled:Pumpkin.BMP", "Happy Halloween!", "Creepy", 60, true)]
    [InlineData("July 4th", "flashTogether", 5, 2, "intermittently", "Happy Faces", "bundled:Flag.BMP", "Happy July 4th!", "Arial Black", 48, true)]
    [InlineData("New Year", "bulbChase", 1, 2, "always", "Balloons", "bundled:New Year Clock.BMP", "Happy New Year!", "Impact", 48, false)]
    [InlineData("St. Patrick's Day", "alternating", 5, 1, "intermittently", "Shamrocks", "bundled:Pot of Gold.BMP", "Happy St. Patrick's Day!", "Georgia Ref", 48, true)]
    [InlineData("Thanksgiving", "bulbChase", 5, 0, "always", "Leaves", "bundled:Turkey.BMP", "Happy Thanksgiving!", "Figaro MT", 60, true)]
    [InlineData("Valentine's Day", "flashTogether", 5, 2, "intermittently", "Valentine's Hearts", "bundled:Hearts.BMP", "Happy Valentine's Day!", "Mistral", 48, true)]
    [InlineData("Autumn Harvest", "alternating", 6, 3, "intermittently", "Leaves", "bundled:Pumpkin.BMP", "Happy Autumn!", "Georgia", 42, true)]
    [InlineData("Bubble Lights", "flashTogether", 4, 31, "always", "Baubles", "bundled:Snowman.BMP", "Happy Holidays!", "Georgia", 42, true)]
    [InlineData("Christmas Twinkle", "twinkle", 5, 31, "always", "Snow", "bundled:Santa Candle.BMP", "Merry Christmas!", "Segoe Script", 48, true)]
    [InlineData("Classic Lights", "flashTogether", 5, 5, "intermittently", "Balloons", "(None)", "", "Arial", 36, true)]
    [InlineData("Holiday Party", "danceToMusic", 3, 8, "always", "Balloons", "(None)", "Let's Celebrate!", "Arial Black", 48, false)]
    [InlineData("Spring Garden", "slowGlow", 5, 3, "intermittently", "Balloons", "(None)", "Happy Spring!", "Comic Sans MS", 42, true)]
    [InlineData("Summer Nights", "twinkle", 7, 3, "intermittently", "Heavens Above", "(None)", "Enjoy the Summer Nights!", "Georgia", 42, false)]
    [InlineData("Winter Wonderland", "slowGlow", 6, 3, "intermittently", "Snow Flakes", "bundled:Snowman.BMP", "Let It Snow!", "Georgia", 42, false)]
    public void Themes_HoldTheValuesOfTheSpecTable(string name, string pattern, int interval, int songs, string mode, string animation, string picture, string message, string font, int size, bool bold)
    {
        ThemeDefinition theme = ShippedThemes.Find(name)!;
        using JsonDocument json = JsonDocument.Parse(HolidayLightsJson.Serialize(theme));
        JsonElement root = json.RootElement;

        Assert.Equal(pattern, root.GetProperty("flash").GetProperty("pattern").GetString());
        Assert.Equal(interval, theme.Flash!.Interval);
        Assert.Equal(songs, theme.Music!.EnabledSongs!.Count);
        Assert.Equal(mode, root.GetProperty("music").GetProperty("mode").GetString());
        Assert.Equal(animation, theme.Saver!.Animation);
        Assert.Equal(picture, theme.Saver.Picture);
        Assert.Equal(PicturePlacement.Center, theme.Saver.Placement);
        Assert.Equal(message, theme.Saver.Message);
        Assert.Equal((font, size, bold), (theme.Saver.Font!.Family, theme.Saver.Font.SizePt, theme.Saver.Font.Bold));
    }

    [Fact]
    public void NewThemes_StateEveryValueIncludingStyleAndBackground()
    {
        Assert.All(ShippedThemes.All.Where(t => t.Shipped == ShippedThemeKind.New), t =>
        {
            Assert.NotNull(t.Saver!.Style);
            Assert.NotNull(t.Saver.Background);
            if (SaverAnimations.SelectsStyle(t.Saver.Animation!))
            {
                Assert.Equal(SaverAnimations.DefaultStyleFor(t.Saver.Animation!), t.Saver.Style);
            }
        });
        Assert.Equal(new RgbColor(0x0B, 0x15, 0x30), ShippedThemes.Find("Holiday Party")!.Saver!.Background);
        Assert.Equal(new RgbColor(0x1D, 0x34, 0x66), ShippedThemes.Find("Spring Garden")!.Saver!.Background);
        Assert.True(ShippedThemes.Find("Summer Nights")!.Saver!.Font!.Italic);
        Assert.Equal(new RgbColor(0xCF, 0xE8, 0xFF), ShippedThemes.Find("Winter Wonderland")!.Saver!.Color);
        Assert.Equal(ShippedThemes.Find("Christmas 1")!.Music!.EnabledSongs, ShippedThemes.Find("Bubble Lights")!.Music!.EnabledSongs);
    }

    [Fact]
    public void Arrangements_FollowTheSpecTable()
    {
        Assert.Equal(SlotAssignment.Classic54Default, ShippedThemes.Find("Christmas 1")!.Arrangement);
        Assert.True(ShippedThemes.Find("Blank Slate")!.Arrangement!.HasNoBulbs());

        SlotAssignment halloween = ShippedThemes.Find("Halloween")!.Arrangement!;
        Assert.Equal(["builtin:jack-o-lanterns", "builtin:ghosts"], halloween.Top);
        Assert.Equal(("builtin:autumn-leaves", "builtin:autumn-leaves", (string?)null, (string?)null), (halloween.TopLeft, halloween.TopRight, halloween.BottomLeft, halloween.BottomRight));

        SlotAssignment chanukah = ShippedThemes.Find("Chanukah")!.Arrangement!;
        Assert.Equal(["builtin:dreidels", "builtin:16-pixel-spacer", "builtin:menorahs", "builtin:16-pixel-spacer"], chanukah.Top);

        SlotAssignment winter = ShippedThemes.Find("Winter Wonderland")!.Arrangement!;
        Assert.Equal(["addon:IcicleLights"], winter.Top);
        Assert.Equal(["addon:Snowflakes4"], winter.Right);
        Assert.Equal(["builtin:snow-family"], winter.Bottom);
        Assert.Equal("addon:PastelSnowflakes", winter.BottomRight);
    }

    [Fact]
    public void EveryShippedTheme_UsesBulbsThatExistWithArtForEverySlotTheyUse()
    {
        using JsonDocument builtIn = GoldenData.ReadJson("builtin-cells.json");
        using JsonDocument addOns = GoldenData.ReadJson("bul-frames.json.gz");
        JsonElement builtInBulbs = builtIn.RootElement.GetProperty("bulbs");
        Dictionary<string, JsonElement> files = addOns.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f.GetProperty("file").GetString()!), StringComparer.OrdinalIgnoreCase);

        foreach (ThemeDefinition theme in ShippedThemes.All)
        {
            SlotAssignment arrangement = theme.Arrangement!;
            foreach (Side side in CellSlots.Sides)
            {
                foreach (string id in arrangement.GetEdge(side))
                {
                    AssertArt(theme.Name, id, slot: (int)side, corner: false);
                }
            }

            foreach (Corner corner in CellSlots.Corners)
            {
                if (arrangement.GetCorner(corner) is { } id)
                {
                    AssertArt(theme.Name, id, slot: (int)corner, corner: true);
                }
            }
        }

        void AssertArt(string theme, string id, int slot, bool corner)
        {
            Assert.True(BulbIds.TryGetOrigin(id, out BulbOrigin origin), $"{theme}: {id}");
            string key = BulbIds.GetKey(id);
            if (origin == BulbOrigin.BuiltIn)
            {
                Assert.True(builtInBulbs.TryGetProperty(key, out JsonElement bulb), $"{theme}: built-in {key} does not exist");
                JsonElement cells = corner ? bulb.GetProperty("corners")[slot] : bulb.GetProperty("sides")[slot][0];
                Assert.True(cells.GetArrayLength() > 0, $"{theme}: {key} has no art for slot {slot}");
                foreach (JsonElement cell in cells.EnumerateArray())
                {
                    JsonElement rect = bulb.GetProperty("cells").GetProperty(cell.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)).GetProperty("rect");
                    Assert.True(rect[2].GetInt32() > rect[0].GetInt32() && rect[3].GetInt32() > rect[1].GetInt32(), $"{theme}: {key} has an empty cell");
                }
            }
            else
            {
                Assert.Equal(BulbOrigin.BundledAddOn, origin);
                Assert.True(File.Exists(Path.Combine(TestPaths.ContentFolder, "Bulbs", key + ".bul")), $"{theme}: {key}.bul is not bundled");
                JsonElement file = files[key];
                Assert.Equal(0, file.GetProperty("loaderErrors").GetArrayLength());
                JsonElement slots = file.GetProperty("slots");
                int entry = corner ? slots.GetProperty("corners")[slot].GetInt32() : slots.GetProperty("sides")[slot][0].GetInt32();
                Assert.Contains(file.GetProperty("entries").EnumerateArray(), e => e.GetProperty("index").GetInt32() == entry && e.GetProperty("frames").GetInt32() > 0);
            }
        }
    }

    [Fact]
    public void EveryShippedTheme_UsesBundledSongsPicturesAndKnownAnimations()
    {
        foreach (ThemeDefinition theme in ShippedThemes.All)
        {
            foreach (string song in theme.Music!.EnabledSongs!)
            {
                Assert.True(MediaIds.TryParse(song, out MediaOrigin origin, out string file) && origin == MediaOrigin.Bundled, $"{theme.Name}: {song}");
                Assert.True(File.Exists(Path.Combine(TestPaths.ContentFolder, "Music", file)), $"{theme.Name}: {file} is not bundled");
            }

            string picture = theme.Saver!.Picture!;
            if (picture != SaverPictures.None)
            {
                Assert.True(MediaIds.TryParse(picture, out MediaOrigin origin, out string file) && origin == MediaOrigin.Bundled, $"{theme.Name}: {picture}");
                Assert.True(File.Exists(Path.Combine(TestPaths.ContentFolder, "Pictures", file)), $"{theme.Name}: {file} is not bundled");
            }

            Assert.Contains(theme.Saver.Animation, SaverAnimations.All);
        }
    }

    [Fact]
    public void Fonts_NewThemesUseWindows11Fonts_ClassicThemesHaveALookAlikeOrAreWindowsFonts()
    {
        foreach (ThemeDefinition theme in ShippedThemes.All)
        {
            string family = theme.Saver!.Font!.Family;
            bool windowsFont = Windows11Fonts.Contains(family);
            if (theme.Shipped == ShippedThemeKind.New)
            {
                Assert.True(windowsFont, $"{theme.Name}: {family} does not ship with Windows 11");
            }
            else
            {
                Assert.True(windowsFont || SaverFontSubstitutes.LookAlikesOf(family).Count > 0, $"{theme.Name}: {family} has no look-alike");
            }
        }
    }
}
