using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// The xUnit collection of the Ui tests that build WPF windows, pages and resources: they run one at a time, never beside
/// other tests, and all on the one <see cref="UiThread"/> (the app itself has a single UI thread). WPF loads XAML through
/// a process-wide schema context that is not safe for several UI threads loading at once, and its text services must not
/// be spread over several threads (see <see cref="UiThread"/>).
/// </summary>
[CollectionDefinition(nameof(WpfCollection), DisableParallelization = true)]
public sealed class WpfCollection : ICollectionFixture<UiThread>;
