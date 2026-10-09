using System.Windows;
using System.Windows.Threading;

namespace HolidayLights.App.BulbFactory;

/// <summary>The "Bulb Credits" window (PRODUCT-SPEC 3.3.3): its preview animates at the flash speed.</summary>
internal sealed partial class BulbCreditsWindow : Window
{
    /// <summary>Creates the window.</summary>
    /// <param name="viewModel">The credits.</param>
    /// <param name="stepInterval">The flash speed.</param>
    public BulbCreditsWindow(BulbCreditsViewModel viewModel, TimeSpan stepInterval)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = stepInterval };
        timer.Tick += (_, _) => ViewModel.Tick();
        Loaded += async (_, _) =>
        {
            timer.Start();
            await ViewModel.LoadAsync();
        };
        Closed += (_, _) => timer.Stop();
    }

    /// <summary>The credits.</summary>
    public BulbCreditsViewModel ViewModel { get; }
}
