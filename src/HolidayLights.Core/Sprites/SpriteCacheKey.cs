using System.Globalization;

namespace HolidayLights.Core.Sprites;

/// <summary>What a cache entry holds.</summary>
internal enum SpriteKind : byte
{
    /// <summary>One scaled frame (<see cref="ISpriteProvider.GetSprite"/>).</summary>
    Frame = 0,

    /// <summary>The glow halo of a light-bulb animation (<see cref="ISpriteProvider.GetGlow"/>).</summary>
    Glow = 1,
}

/// <summary>
/// The key of one cached sprite or halo. Frames use the contract's <see cref="SpriteKey"/>; a halo depends only on the
/// bulb content, slot, flavor and scale, so its frame is 0 and its style Smooth (the frames a halo is baked from).
/// </summary>
/// <param name="Sprite">The sprite key (normalized flavor and frame).</param>
/// <param name="Kind">Frame or glow.</param>
internal readonly record struct SpriteCacheKey(SpriteKey Sprite, SpriteKind Kind)
{
    /// <summary>The bulb content key.</summary>
    public string ContentKey => Sprite.ContentKey;

    /// <summary>The key of a scaled frame.</summary>
    /// <param name="sprite">The normalized sprite key.</param>
    /// <returns>The cache key.</returns>
    public static SpriteCacheKey ForFrame(SpriteKey sprite) => new(sprite, SpriteKind.Frame);

    /// <summary>The key of a glow halo.</summary>
    /// <param name="contentKey">The bulb content key.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">The normalized flavor.</param>
    /// <param name="scale">S.</param>
    /// <returns>The cache key.</returns>
    public static SpriteCacheKey ForGlow(string contentKey, CellSlot slot, int flavor, double scale) =>
        new(new SpriteKey(contentKey, slot, flavor, 0, scale, SpriteStyle.Smooth), SpriteKind.Glow);

    /// <summary>A file name that is unique within the content key's folder (the scale as its exact bit pattern).</summary>
    /// <returns>For example <c>frame-0-2-1-3ff8000000000000-smooth.sprite</c>.</returns>
    public string ToFileName() => Kind == SpriteKind.Frame
        ? string.Create(CultureInfo.InvariantCulture,
            $"frame-{(int)Sprite.Slot}-{Sprite.Flavor}-{Sprite.Frame}-{ScaleBits:x16}-{StyleName}.sprite")
        : string.Create(CultureInfo.InvariantCulture,
            $"glow-{(int)Sprite.Slot}-{Sprite.Flavor}-{ScaleBits:x16}.sprite");

    /// <summary>The complete key as text, stored in cache files and compared when they are read back.</summary>
    /// <returns>Every field of the key, '|'-separated.</returns>
    public string ToCanonicalString() => string.Create(CultureInfo.InvariantCulture,
        $"{Kind}|{ContentKey}|{(int)Sprite.Slot}|{Sprite.Flavor}|{Sprite.Frame}|{ScaleBits:x16}|{StyleName}");

    private long ScaleBits => BitConverter.DoubleToInt64Bits(Sprite.Scale);

    private string StyleName => Sprite.Style == SpriteStyle.Crisp ? "crisp" : "smooth";
}
