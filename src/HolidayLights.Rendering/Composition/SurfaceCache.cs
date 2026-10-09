using HolidayLights.Rendering.Scenes;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Composition;

/// <summary>
/// The uploaded sprites: every unique (bulb, cell, scale, style) frame and glow halo is uploaded once into its own surface and
/// shared by every visual that shows it (ARCHITECTURE 5). Surfaces no visual uses any more are released by <see cref="Retain"/>.
/// </summary>
internal sealed class SurfaceCache : IDisposable
{
    private readonly CompositionDevice device;
    private readonly Dictionary<SpriteId, IDCompositionSurface?> surfaces = [];

    /// <summary>Creates the cache for a device.</summary>
    public SurfaceCache(CompositionDevice device) => this.device = device;

    /// <summary>Number of uploaded surfaces.</summary>
    public int Count => surfaces.Count;

    /// <summary>Returns the surface of a sprite, uploading it from <paramref name="images"/> the first time (null for an empty or missing picture).</summary>
    public IDCompositionSurface? Get(SpriteId id, IReadOnlyDictionary<SpriteId, PremultipliedImage> images)
    {
        if (surfaces.TryGetValue(id, out IDCompositionSurface? surface))
        {
            return surface;
        }

        surface = images.TryGetValue(id, out PremultipliedImage? image) ? device.CreateSurface(image) : null;
        surfaces[id] = surface;
        return surface;
    }

    /// <summary>Releases every surface whose sprite is not in <paramref name="live"/>.</summary>
    public void Retain(IReadOnlySet<SpriteId> live)
    {
        foreach (SpriteId id in surfaces.Keys.Where(id => !live.Contains(id)).ToList())
        {
            surfaces[id]?.Dispose();
            surfaces.Remove(id);
        }
    }

    /// <summary>Releases every surface.</summary>
    public void Dispose()
    {
        foreach (IDCompositionSurface? surface in surfaces.Values)
        {
            surface?.Dispose();
        }

        surfaces.Clear();
    }
}
