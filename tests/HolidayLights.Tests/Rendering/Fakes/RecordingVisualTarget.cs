using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;

namespace HolidayLights.Tests.Rendering.Fakes;

/// <summary>Records what the animator sends, per ordinal (the latest values) and as a call log.</summary>
internal sealed class RecordingVisualTarget : IBulbVisualTarget
{
    public Dictionary<int, int> Frames { get; } = [];

    public Dictionary<int, OpacityCurve> Lit { get; } = [];

    public Dictionary<int, OpacityCurve> Glow { get; } = [];

    public Dictionary<int, OpacityCurve> Opacity { get; } = [];

    public List<string> Calls { get; } = [];

    public void ShowFrame(int ordinal, int frame)
    {
        Frames[ordinal] = frame;
        Calls.Add($"frame {ordinal} {frame}");
    }

    public void SetLit(int ordinal, OpacityCurve curve)
    {
        Lit[ordinal] = curve;
        Calls.Add($"lit {ordinal}");
    }

    public void SetGlow(int ordinal, OpacityCurve curve)
    {
        Glow[ordinal] = curve;
        Calls.Add($"glow {ordinal}");
    }

    public void SetOpacity(int ordinal, OpacityCurve curve)
    {
        Opacity[ordinal] = curve;
        Calls.Add($"opacity {ordinal}");
    }

    public void Clear()
    {
        Frames.Clear();
        Lit.Clear();
        Glow.Clear();
        Opacity.Clear();
        Calls.Clear();
    }
}
