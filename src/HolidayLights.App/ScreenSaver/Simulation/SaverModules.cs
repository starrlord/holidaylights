using HolidayLights.App.ScreenSaver.Art;

namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>Creates the module of an animation value (5.4 <c>Saver_Start</c>: "(None)", Snow, Snow Flakes, Balloons, the table, an add-on bulb).</summary>
internal static class SaverModules
{
    /// <summary>Creates the module, drawing its start positions from the random stream.</summary>
    /// <param name="animation">The animation value (<see cref="SaverAnimations"/>).</param>
    /// <param name="style">The movement style (used by the floater animations only).</param>
    /// <param name="field">The simulated screen.</param>
    /// <param name="random">The random stream.</param>
    /// <param name="bulbs">Resolves an add-on bulb animation.</param>
    /// <returns>The module, or null for "(None)", an unknown name or a bulb that is gone or has no pictures.</returns>
    public static SaverModule? Create(string animation, SaverMovementStyle style, SaverField field, ISaverRandom random, IBulbResolver bulbs)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(bulbs);
        switch (animation)
        {
            case SaverAnimations.None:
                return null;
            case SaverAnimations.Snow:
                return new SnowModule(field, random, SnowArt.Shapes);
            case SaverAnimations.SnowFlakes:
                return new SnowModule(field, random, SnowArt.Flakes);
            case SaverAnimations.Balloons:
                return new BalloonModule(field, random, SaverArtLibrary.Balloons);
        }

        IReadOnlyList<SpriteArt> arts =
            SaverAnimationTable.TryGet(animation, out SaverAnimationEntry? entry) ? SaverArtLibrary.ForTableAnimation(entry)
            : SaverAnimations.TryGetBulbId(animation, out string? bulbId) && bulbs.TryGetBulb(bulbId, out IBulb? bulb) ? SaverArtLibrary.ForAddOnBulb(bulb)
            : [];
        return arts.Count == 0 ? null : new FloaterModule(field, random, style, arts);
    }
}
