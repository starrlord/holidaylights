using HolidayLights.Core.Flash;

namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>The random numbers of the saver simulation: the stream of the CRT <c>rand()</c> that 5.4 used (0 to 32,767).</summary>
internal interface ISaverRandom
{
    /// <summary>Returns the next number.</summary>
    /// <returns>0 to 32,767.</returns>
    int Next();
}

/// <summary>The MSVC CRT <c>rand()</c> (<c>seed x 214013 + 2531011</c>), exactly as 5.4 drew its numbers.</summary>
/// <param name="seed">The <c>srand</c> seed.</param>
internal sealed class CrtSaverRandom(uint seed) : ISaverRandom
{
    private readonly MsvcRandom random = new(seed);

    /// <inheritdoc />
    public int Next() => random.Next();
}

/// <summary>The C expressions 5.4 builds from <c>rand()</c>, with the same number of calls.</summary>
internal static class SaverRandomExtensions
{
    /// <summary>
    /// <c>rand() % modulus</c>. One number is always drawn, so the stream stays in step with 5.4; a modulus of zero or less
    /// (a sprite larger than the screen, where 5.4 divided by zero) gives 0.
    /// </summary>
    /// <param name="random">The stream.</param>
    /// <param name="modulus">The modulus.</param>
    /// <returns>0 to <paramref name="modulus"/> - 1.</returns>
    public static int NextModulo(this ISaverRandom random, int modulus)
    {
        int value = random.Next();
        return modulus <= 0 ? 0 : value % modulus;
    }

    /// <summary><c>(rand() % 1001 - 500) x factor</c>: the random walks of wind, speed and drift.</summary>
    /// <param name="random">The stream.</param>
    /// <param name="factor">The step size (0.0002 for wind and speed).</param>
    /// <returns>A value from -500 x factor to 500 x factor.</returns>
    public static double NextWalk(this ISaverRandom random, double factor) => (random.Next() % 1001 - 500) * factor;
}
