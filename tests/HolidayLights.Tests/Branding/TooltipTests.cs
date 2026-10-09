using System.Collections;
using System.Windows;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Branding;

/// <summary>
/// The description and tooltip texts of <c>Assets/Tooltips.xaml</c>: every text of PRODUCT-SPEC Appendix A word for word
/// (the 5.4 "What's This?" popups, updated), reachable through the dictionary App.xaml merges.
/// </summary>
public sealed class TooltipTests
{
    /// <summary>PRODUCT-SPEC Appendix A, one entry per control (rows that cover several controls are split).</summary>
    public static TheoryData<string, string> AppendixA() => new()
    {
        { "HL.Tip.BulbFactory.BulbList", "Shows the bulbs you can use. Double-click a bulb to use it, or drag it into the boxes around your screen. Right-click a bulb to edit it, see its credits, or remove a bulb you've added. (Bulbs that came with Holiday Lights 5.4 can't be removed.)" },
        { "HL.Tip.BulbFactory.TopLeftCorner", "Represents the top-left corner of your screen. Drag a bulb here, or select this box and double-click a bulb." },
        { "HL.Tip.BulbFactory.TopRightCorner", "Represents the top-right corner of your screen. Drag a bulb here, or select this box and double-click a bulb." },
        { "HL.Tip.BulbFactory.BottomLeftCorner", "Represents the bottom-left corner of your screen. Drag a bulb here, or select this box and double-click a bulb." },
        { "HL.Tip.BulbFactory.BottomRightCorner", "Represents the bottom-right corner of your screen. Drag a bulb here, or select this box and double-click a bulb." },
        { "HL.Tip.BulbFactory.TopEdge", "Represents the top edge of your screen. Up to six kinds of bulbs repeat along the edge in this order." },
        { "HL.Tip.BulbFactory.RightEdge", "Represents the right edge of your screen. Up to six kinds of bulbs repeat along the edge in this order." },
        { "HL.Tip.BulbFactory.BottomEdge", "Represents the bottom edge of your screen. Up to six kinds of bulbs repeat along the edge in this order." },
        { "HL.Tip.BulbFactory.LeftEdge", "Represents the left edge of your screen. Up to six kinds of bulbs repeat along the edge in this order." },
        { "HL.Tip.BulbFactory.WholeFrame", "Double-clicking a bulb puts it on every edge and in every corner." },
        { "HL.Tip.BulbFactory.AllEdges", "Double-clicking a bulb puts it on all four edges; the corners stay." },
        { "HL.Tip.BulbFactory.AllCorners", "Double-clicking a bulb puts it in all four corners; the edges stay." },
        { "HL.Tip.BulbFactory.Peek", "Hide this window for a moment so you can see your desktop. Hold to keep it hidden." },
        { "HL.Tip.BulbFactory.ClearAllBulbs", "Takes every bulb off the screen. You can undo it." },
        { "HL.Tip.FlashSettings.Pattern", "Changes the pattern in which the bulbs flash." },
        { "HL.Tip.FlashSettings.Speed", "Changes the speed at which the bulbs flash. Slower speeds use less of your computer's power." },
        { "HL.Tip.FlashSettings.SmoothFading", "Light bulbs fade on and off like real bulbs instead of switching instantly." },
        { "HL.Tip.BulbFactory.AddBulb", "Adds a bulb file (.bul) to the list, or turns an animated GIF picture into a new bulb." },
        { "HL.Tip.BulbFactory.EditBulb", "If you made the bulb yourself, you can edit its name, description, pictures and more. If someone else made it, you can edit its categories." },
        { "HL.Tip.BulbFactory.BulbCredits", "Shows who made this bulb and its copyright." },
        { "HL.Tip.BulbDrawing.OnDesktop", "Shows the bulbs on your desktop, below all your windows, so they never get in your way." },
        { "HL.Tip.BulbDrawing.BehindIcons", "Your desktop icons stay in front of the bulbs and keep working. Uncheck it to draw the bulbs in front of the icons, still behind every window." },
        { "HL.Tip.BulbDrawing.OnTop", "Shows the bulbs above all windows so they're always visible. Clicks go through them, and they hide while a full-screen app is open." },
        { "HL.Tip.Themes.LoadTheme", "Replaces your bulb, music and screen saver settings with a saved theme." },
        { "HL.Tip.Themes.SaveTheme", "Saves your current bulb, music and screen saver settings as a theme." },
        { "HL.Tip.Look.BulbSize", "How big the bulbs are, compared with everything else on each display." },
        { "HL.Tip.Look.Look", "Modern Glow: smooth pixels, a soft glow and fading. Bright Glow: a stronger glow. Classic 2003: exactly like Holiday Lights 5.4." },
        { "HL.Tip.Look.Pixels", "Smooth keeps the hand-drawn look at any size. Crisp shows square pixels, like 2003." },
        { "HL.Tip.Look.Glow", "Lit bulbs give off a soft light that brightens the wallpaper around them." },
        { "HL.Tip.Displays.ShowOn", "Choose which displays get lights." },
        { "HL.Tip.Displays.Identify", "Shows each display's number on the display." },
        { "HL.Tip.Music.PlayHolidayMusic", "Turns all music on or off. Themes can't turn music on while this is off." },
        { "HL.Tip.Music.SongCheckBox", "Controls whether Holiday Lights ever picks this song when randomly picking music." },
        { "HL.Tip.Music.PlayButton", "Plays this song now." },
        { "HL.Tip.Music.AddSong", "Adds songs to the list. The files are copied to your My Music folder so they're always available." },
        { "HL.Tip.Music.Never", "Prevents Holiday Lights from playing music at any time." },
        { "HL.Tip.Music.OnlyWhenSaverOn", "Plays music only when the Holiday Lights screen saver is showing." },
        { "HL.Tip.Music.OnlyWhenSaverOff", "Plays music only when the Holiday Lights screen saver is not showing." },
        { "HL.Tip.Music.Always", "Plays music in the background at all times." },
        { "HL.Tip.Music.Intermittently", "Plays a song in the background at one to three minute random intervals." },
        { "HL.Tip.Music.Volume", "Changes how loud Holiday Lights plays music. Other apps aren't affected." },
        { "HL.Tip.Music.DanceToMusic", "The lights play along with the music. It changes the flash pattern in the Bulb Factory." },
        { "HL.Tip.Saver.TextMessage", "Type any text you want to appear on the screen saver." },
        { "HL.Tip.Saver.Clear", "Removes the text message from the screen saver." },
        { "HL.Tip.Saver.TextFormat", "Change the font, size, style and color in which the text message appears." },
        { "HL.Tip.Saver.Animation", "Animations appear in addition to the bulbs around the edge of the screen. Click an animation to choose it." },
        { "HL.Tip.Saver.UseBulbAsAnimation", "Lets one of your add-on bulbs float around the screen saver." },
        { "HL.Tip.Saver.Style", "Chooses a different movement style for the animation. If this box is dimmed, the animation has its own movement that can't be changed." },
        { "HL.Tip.Saver.Picture", "Chooses the background picture. Right-click a picture to remove it." },
        { "HL.Tip.Saver.Placement", "Center puts the picture in the middle; Tile repeats it; Stretch makes it the same size as your screen, even if that distorts it." },
        { "HL.Tip.Saver.AddPicture", "Adds a picture. It's copied to your My Pictures folder." },
        { "HL.Tip.Saver.BackgroundColor", "The color around the picture, or of the whole background if there's no picture. A picture that covers the screen hides it." },
        { "HL.Tip.Saver.Preview", "Shows the screen saver now, with the current settings. It doesn't change your Windows screen saver." },
        { "HL.Tip.Saver.UseAsScreenSaver", "Makes Holiday Lights your Windows screen saver. Stop Using It brings back the one you had." },
        { "HL.Tip.Saver.ShowOn", "All Displays shows the screen saver on every display. Main Display Only leaves the others black, like Holiday Lights 5.4." },
        { "HL.Tip.Saver.SmoothMotion", "Moves snow, balloons and text smoothly. Turn it off for the 2003 look." },
        { "HL.Tip.Themes.Automatic", "On a holiday, its theme replaces your bulb, music and screen saver settings. Your previous settings are kept in Recent Settings." },
        { "HL.Tip.General.AutoStart", "Controls whether Holiday Lights runs automatically each time you sign in to Windows." },
        { "HL.Tip.General.HotKey", "This is the key combination Holiday Lights uses. To change it, click Change… and press the new keys." },
        { "HL.Tip.General.LimitFlashing", "Keeps the lights flashing slowly enough to be comfortable for people who are sensitive to flashing lights." },
        { "HL.Tip.Window.OK", "Closes this window and keeps your changes." },
        { "HL.Tip.Window.Cancel", "Closes this window and undoes every change made since you opened it. Things you added or saved are kept." },
        { "HL.Tip.Window.Help", "Shows help for this page." },
        { "HL.Tip.Window.Undo", "Undo: {0}" },
        { "HL.Tip.Window.Redo", "Redo: {0}" },
        { "HL.Tip.BulbEditing.Name", "Type the name of the bulb. It's shown in the bulb list." },
        { "HL.Tip.BulbEditing.Description", "Type the description of the bulb. It's shown in the bulb list." },
        { "HL.Tip.BulbEditing.Author", "Your name and e-mail address. They're shown when someone views the bulb credits." },
        { "HL.Tip.BulbEditing.Copyright", "A copyright notice. It's shown when someone views the bulb credits." },
        { "HL.Tip.BulbEditing.Categories", "Check the categories this bulb belongs to. You can then show a single category in the bulb list." },
        { "HL.Tip.BulbEditing.NewCategory", "Adds a new category to the list." },
        { "HL.Tip.BulbEditing.Slot", "You can use different GIF animations for different sides or corners. Choose the one you want to preview or change." },
        { "HL.Tip.BulbEditing.Flavor", "A side can have up to eight different GIF animations. Each animation is called a flavor; they repeat along the edge. If this list is dimmed, you're viewing a corner or the bulb list preview, which have one animation each." },
        { "HL.Tip.BulbEditing.Preview", "Shows the selected side or corner. With Bulb List Preview selected, drag the white square (or use the arrow keys) to choose the 32 x 32 picture shown in the bulb list." },
        { "HL.Tip.BulbEditing.Change", "Chooses a new GIF animation for the selected side and flavor." },
        { "HL.Tip.BulbEditing.RemoveFlavor", "Removes the animation for the selected flavor. If this button is dimmed, the selected flavor can't be removed." },
    };

    [Theory]
    [MemberData(nameof(AppendixA))]
    public void AppendixATextIsAvailableToEveryWindow(string key, string text) => StaThread.Run(() =>
        Assert.Equal(text, IllustrationTests.LoadDictionary()[key]));

    [Fact]
    public void EveryTooltipIsOneCleanSentenceOrFormat() => StaThread.Run(() =>
    {
        ResourceDictionary tooltips = IllustrationTests.LoadDictionary().MergedDictionaries.Single();
        var entries = tooltips.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => Assert.IsType<string>(e.Value));

        Assert.True(entries.Count >= AppendixA().Count() + 40, $"only {entries.Count} tooltips");
        Assert.All(entries, e =>
        {
            Assert.StartsWith("HL.Tip.", e.Key, StringComparison.Ordinal);
            Assert.Matches(@"^[A-Z(][^\r\n\t]*[.)}]$", e.Value);
            Assert.DoesNotContain("...", e.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("  ", e.Value, StringComparison.Ordinal);
        });
    });
}
