namespace HolidayLights.Core.Sprites;

/// <summary>Cache budgets of a <see cref="SpriteProvider"/>.</summary>
public sealed record SpriteProviderOptions
{
    /// <summary>The defaults: 48 MB of sprites in memory, 256 MB on disk.</summary>
    public static SpriteProviderOptions Default { get; } = new();

    /// <summary>
    /// The memory the cached sprites and halos may use before the least recently used are dropped (the working-set goal
    /// of PRODUCT-SPEC G5 is under 200 MB for the whole app).
    /// </summary>
    public long MemoryBudgetBytes { get; init; } = 48L * 1024 * 1024;

    /// <summary>The disk space of the sprite cache before the least recently used files are deleted.</summary>
    public long DiskBudgetBytes { get; init; } = 256L * 1024 * 1024;
}
