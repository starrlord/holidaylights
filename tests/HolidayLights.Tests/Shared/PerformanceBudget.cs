namespace HolidayLights.Tests.Shared;

/// <summary>
/// The time budgets of the performance tests (owner: contracts). Each budget is the one for the reference PC, where the
/// tests pass with a wide margin. GitHub's shared runners are slower and their speed varies from run to run (indexing
/// the bundled bulbs once took 2.1 s there against its 1.5 s budget, and passed when run again), so on GitHub Actions
/// every budget is <see cref="RunnerFactor"/> times longer: a real slowdown still fails there, a busy runner does not.
/// </summary>
public static class PerformanceBudget
{
    /// <summary>How much longer budgets are on a GitHub-hosted runner.</summary>
    public const double RunnerFactor = 3;

    /// <summary>True when the tests run on GitHub Actions (<c>GITHUB_ACTIONS=true</c>).</summary>
    public static bool OnSharedRunner { get; } =
        string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>A budget in milliseconds for this machine.</summary>
    /// <param name="referenceMilliseconds">The budget on the reference PC.</param>
    /// <returns>The budget here.</returns>
    public static double Milliseconds(double referenceMilliseconds) => OnSharedRunner ? referenceMilliseconds * RunnerFactor : referenceMilliseconds;

    /// <summary>A budget for this machine.</summary>
    /// <param name="reference">The budget on the reference PC.</param>
    /// <returns>The budget here.</returns>
    public static TimeSpan Of(TimeSpan reference) => TimeSpan.FromMilliseconds(Milliseconds(reference.TotalMilliseconds));
}
