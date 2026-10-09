namespace HolidayLights.Tests.Rendering.Fakes;

/// <summary>Resolves the fake bulbs.</summary>
internal sealed class FakeBulbResolver : IBulbResolver
{
    private readonly Dictionary<string, IBulb> bulbs = new(BulbIds.Comparer);

    public FakeBulbResolver(params IBulb[] bulbs)
    {
        foreach (IBulb bulb in bulbs)
        {
            this.bulbs[bulb.Id] = bulb;
        }
    }

    public bool TryGetBulb(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBulb? bulb) => bulbs.TryGetValue(id, out bulb);
}
