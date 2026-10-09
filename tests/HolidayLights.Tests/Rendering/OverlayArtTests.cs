using HolidayLights.Rendering.Overlays;

namespace HolidayLights.Tests.Rendering;

public sealed class OverlayArtTests
{
    [Fact]
    public void Pill_Is36DipHighWithARoundedDarkBackgroundAndWhiteText()
    {
        PremultipliedImage pill = OverlayArt.Pill(new PillRequest(PillGlyph.OnTop, "Bulbs on top of all windows"), showSecondLine: false, dpi: 144);

        Assert.Equal(54, pill.Height);
        Assert.InRange(pill.Width, 200, 600);

        // The corners are transparent (18 DIP radius), the middle of the left edge is the #202020 background at 92 %.
        Assert.Equal(0u, pill[0, 0]);
        Assert.Equal(0u, pill[pill.Width - 1, pill.Height - 1]);
        uint background = pill[3, pill.Height / 2];
        Assert.InRange(Bgra32.A(background), 230, 240);
        Assert.InRange(Bgra32.R(background), 25, 45);

        // Somewhere there is (nearly) white text, and colour never exceeds alpha (no additive pixels in the pill).
        Assert.Contains(pill.Pixels, p => Bgra32.A(p) > 240 && Bgra32.R(p) > 240);
        Assert.All(pill.Pixels, p => Assert.True(Bgra32.R(p) <= Bgra32.A(p)));
    }

    [Fact]
    public void Pill_GrowsForLongerTextAndASecondLine()
    {
        PremultipliedImage shortPill = OverlayArt.Pill(new PillRequest(PillGlyph.LightsOn, "Lights on"), false, 96);
        PremultipliedImage longPill = OverlayArt.Pill(new PillRequest(PillGlyph.OnDesktop, "Bulbs on the desktop"), false, 96);
        var hint = new PillRequest(PillGlyph.OnTop, "Bulbs on top of all windows", "Press Ctrl+Alt+Shift+B again to put them back.", TimeSpan.FromSeconds(4));
        PremultipliedImage twoLines = OverlayArt.Pill(hint, showSecondLine: true, 96);

        Assert.Equal(36, shortPill.Height);
        Assert.True(longPill.Width > shortPill.Width);
        Assert.Equal(56, twoLines.Height);
        Assert.True(twoLines.Width > OverlayArt.Pill(hint, showSecondLine: false, 96).Width);
    }

    [Fact]
    public void Pill_ScalesWithTheDisplay()
    {
        var request = new PillRequest(PillGlyph.LightsOff, "Lights off");
        PremultipliedImage at100 = OverlayArt.Pill(request, false, 96);
        PremultipliedImage at200 = OverlayArt.Pill(request, false, 192);

        Assert.Equal(72, at200.Height);
        Assert.InRange(at200.Width, 2 * at100.Width - 12, 2 * at100.Width + 12);
    }

    [Fact]
    public void Identify_ShowsTheNumberOnASquareCard()
    {
        PremultipliedImage card = OverlayArt.Identify(2, 144);

        Assert.Equal(240, card.Height);
        Assert.Equal(240, card.Width);
        Assert.Contains(card.Pixels, p => Bgra32.A(p) > 240 && Bgra32.R(p) > 240);
        Assert.Equal(0u, card[0, 0]);
        Assert.True(OverlayArt.Identify(12, 144).Width >= card.Width);
    }

    [Fact]
    public void Icons_AreFluentGlyphs()
    {
        Assert.Equal("", OverlayArt.IconOf(PillGlyph.OnTop));
        Assert.Equal("", OverlayArt.IconOf(PillGlyph.OnDesktop));
        Assert.Equal("", OverlayArt.IconOf(PillGlyph.LightsOn));
        Assert.Equal("", OverlayArt.IconOf(PillGlyph.LightsOff));
    }

    [Fact]
    public void RoundedRectangleDistance_IsNegativeInsideAndPositiveOutside()
    {
        Assert.True(OverlayArt.RoundedRectangleDistance(50, 18, 100, 36, 18) < 0);
        Assert.True(OverlayArt.RoundedRectangleDistance(0.5, 0.5, 100, 36, 18) > 0);
        Assert.InRange(OverlayArt.RoundedRectangleDistance(50, 0, 100, 36, 18), -0.001, 0.001);
    }
}
