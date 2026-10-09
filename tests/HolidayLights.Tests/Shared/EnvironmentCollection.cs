namespace HolidayLights.Tests.Shared;

/// <summary>
/// The xUnit collection for tests that change process-wide state (environment variables such as
/// <c>HOLIDAYLIGHTS_DATA_ROOT</c>): its tests never run in parallel with any other test. Owner: contracts.
/// </summary>
[CollectionDefinition(nameof(EnvironmentCollection), DisableParallelization = true)]
public sealed class EnvironmentCollection;
