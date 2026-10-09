using HolidayLights.Rendering.Composition;

namespace HolidayLights.Tests.Rendering;

/// <summary>The glow on displays in Windows HDR is drawn at half intensity (PRODUCT-SPEC 5.9, risk R2; product-owner decision 2).</summary>
public sealed class AdvancedColorTests
{
    [Fact]
    public void GlowScale_IsHalfOnDisplaysInHdrOnly()
    {
        RectI first = RectI.FromXYWH(0, 0, 3840, 2160);
        RectI second = RectI.FromXYWH(-3840, 0, 3840, 2160);
        var hdr = new HashSet<RectI> { second };

        Assert.Equal(1f, AdvancedColorOutputs.GlowScale(hdr, first));
        Assert.Equal(0.5f, AdvancedColorOutputs.GlowScale(hdr, second));
        Assert.Equal(1f, AdvancedColorOutputs.GlowScale(new HashSet<RectI>(), second));
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void Refresh_ReadsTheOutputsOfThisPc()
    {
        // Every desktop has DXGI; outputs in HDR (none on the reference PC) are reported by their desktop rectangle.
        using var outputs = new AdvancedColorOutputs();
        outputs.Refresh(force: true);

        Assert.All(outputs.HdrBounds, bounds => Assert.False(bounds.IsEmpty));
        Assert.All(outputs.HdrBounds, bounds => Assert.Equal(AdvancedColorOutputs.HdrGlowScale, outputs.GlowScale(bounds)));
        outputs.Refresh(force: false);
    }
}
