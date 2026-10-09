namespace HolidayLights.Core.Sprites;

/// <summary>A cached result: a scaled frame, a glow halo, or the fact that a light bulb has no emissive pixels.</summary>
internal sealed class SpriteCacheEntry
{
    /// <summary>Approximate bookkeeping bytes per entry on top of the pixels.</summary>
    private const long OverheadBytes = 160;

    private volatile bool isPersisted;
    private int writing;

    private SpriteCacheEntry(PremultipliedImage? frame, GlowSprite? glow)
    {
        Frame = frame;
        Glow = glow;
    }

    /// <summary>The scaled frame (frame entries).</summary>
    public PremultipliedImage? Frame { get; }

    /// <summary>The halo (glow entries); null for a light bulb without emissive pixels.</summary>
    public GlowSprite? Glow { get; }

    /// <summary>True once the entry is known to be on disk (read from or written to the disk cache).</summary>
    public bool IsPersisted
    {
        get => isPersisted;
        set => isPersisted = value;
    }

    /// <summary>Claims the disk write of the entry, so threads that ask for one sprite at the same moment store it once.</summary>
    /// <returns>True when this caller writes the entry (it then calls <see cref="EndWrite"/>).</returns>
    public bool TryBeginWrite() => Interlocked.CompareExchange(ref writing, 1, 0) == 0;

    /// <summary>Releases the claim of <see cref="TryBeginWrite"/> (after a failed write a later prefetch tries again).</summary>
    public void EndWrite() => Volatile.Write(ref writing, 0);

    /// <summary>The memory the entry accounts for.</summary>
    public long SizeInBytes => OverheadBytes + 4L * ((Frame ?? Glow?.Image)?.Pixels.LongLength ?? 0);

    /// <summary>Creates a frame entry.</summary>
    /// <param name="frame">The scaled frame.</param>
    /// <returns>The entry.</returns>
    public static SpriteCacheEntry ForFrame(PremultipliedImage frame) => new(frame, null);

    /// <summary>Creates a glow entry.</summary>
    /// <param name="glow">The halo, or null when the bulb emits no light.</param>
    /// <returns>The entry.</returns>
    public static SpriteCacheEntry ForGlow(GlowSprite? glow) => new(null, glow);
}
