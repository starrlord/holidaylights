using HolidayLights.Platform.Displays;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

public sealed class DisplayServiceTests
{
    private static readonly TimeSpan LongPoll = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ShortDebounce = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void Constructor_PublishesTheDisplaysAtOnce()
    {
        using var ui = new DispatcherThread();
        var source = new FakeDisplaySource(DisplayTopologyTests.ReferenceSamples);
        DisplayService service = ui.Invoke(() => new DisplayService(new RecordingLog(), source, LongPoll, ShortDebounce));

        Assert.Equal(2, service.Displays.Count);
        Assert.Equal(1, service.Primary.Number);
        Assert.True(service.Primary.IsPrimary);
        Assert.Equal(2, service.FromPoint(new PointI(-100, 100)).Number);
        Assert.Equal(1, service.FromPoint(new PointI(9999, 9999)).Number);
        ui.Invoke(service.Dispose);
    }

    [Fact]
    public void TopologyMessage_RebuildsAfterTheDebounceAndRaisesOnce()
    {
        using var ui = new DispatcherThread();
        var source = new FakeDisplaySource(DisplayTopologyTests.ReferenceSamples);
        DisplayService service = ui.Invoke(() => new DisplayService(new RecordingLog(), source, LongPoll, ShortDebounce));
        var raised = new List<DisplaysChangedEventArgs>();
        service.DisplaysChanged += (_, e) => raised.Add(e);

        // The taskbar of display 2 moves to the top: only its work area changes.
        source.Samples =
        [
            DisplayTopologyTests.ReferenceSamples[0],
            DisplayTopologyTests.ReferenceSamples[1] with { WorkArea = new RectI(-3840, 72, 0, 2160) },
        ];
        ui.Invoke(service.NotifyTopologyMessage);
        ui.Invoke(service.NotifyTopologyMessage);

        Assert.True(DispatcherThread.WaitFor(() => raised.Count > 0, TimeSpan.FromSeconds(5)));
        DispatcherThread.Wait(TimeSpan.FromMilliseconds(300));
        DisplaysChangedEventArgs change = Assert.Single(raised);
        Assert.Equal(new RectI(-3840, 0, 0, 2088), change.Previous[1].WorkArea);
        Assert.Equal(new RectI(-3840, 72, 0, 2160), change.Current[1].WorkArea);
        Assert.Same(change.Current, service.Displays);
        ui.Invoke(service.Dispose);
    }

    [Fact]
    public void TopologyMessage_WithoutAChangeRaisesNothing()
    {
        using var ui = new DispatcherThread();
        var source = new FakeDisplaySource(DisplayTopologyTests.ReferenceSamples);
        DisplayService service = ui.Invoke(() => new DisplayService(new RecordingLog(), source, LongPoll, ShortDebounce));
        IReadOnlyList<DisplayInfo> before = service.Displays;
        int raised = 0;
        service.DisplaysChanged += (_, _) => raised++;

        ui.Invoke(service.NotifyTopologyMessage);
        Assert.True(DispatcherThread.WaitFor(() => source.IdentityReads >= 2, TimeSpan.FromSeconds(5)));
        DispatcherThread.Wait(TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, raised);
        Assert.Same(before, service.Displays);
        ui.Invoke(service.Dispose);
    }

    [Fact]
    public void Poll_NoticesAScaleChangeWithoutAnyMessage()
    {
        using var ui = new DispatcherThread();
        var source = new FakeDisplaySource(DisplayTopologyTests.ReferenceSamples);
        DisplayService service = ui.Invoke(() => new DisplayService(new RecordingLog(), source, TimeSpan.FromMilliseconds(100), ShortDebounce));
        DisplaysChangedEventArgs? change = null;
        service.DisplaysChanged += (_, e) => change = e;

        source.Samples = [DisplayTopologyTests.ReferenceSamples[0], DisplayTopologyTests.ReferenceSamples[1] with { Dpi = 192 }];

        Assert.True(DispatcherThread.WaitFor(() => change is not null, TimeSpan.FromSeconds(5)));
        Assert.Equal(192, change!.Current.Single(d => d.Number == 2).Dpi);
        ui.Invoke(service.Dispose);
    }

    [Fact]
    public void RemovedDisplay_IsReported()
    {
        using var ui = new DispatcherThread();
        var source = new FakeDisplaySource(DisplayTopologyTests.ReferenceSamples);
        DisplayService service = ui.Invoke(() => new DisplayService(new RecordingLog(), source, LongPoll, ShortDebounce));
        DisplaysChangedEventArgs? change = null;
        service.DisplaysChanged += (_, e) => change = e;

        source.Samples = [DisplayTopologyTests.ReferenceSamples[0]];
        ui.Invoke(service.NotifyTopologyMessage);

        Assert.True(DispatcherThread.WaitFor(() => change is not null, TimeSpan.FromSeconds(5)));
        Assert.Equal(2, change!.Previous.Count);
        Assert.Single(service.Displays);
        ui.Invoke(service.Dispose);
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void RealDisplays_AreReadInPhysicalPixels()
    {
        using var ui = new DispatcherThread();
        var log = new RecordingLog();
        DisplayService service = ui.Invoke(() => new DisplayService(log));

        Assert.NotEmpty(service.Displays);
        Assert.Equal(Enumerable.Range(1, service.Displays.Count), service.Displays.Select(d => d.Number));
        Assert.Single(service.Displays, d => d.IsPrimary);
        Assert.Same(service.Displays[0], service.Primary);
        Assert.Equal(service.Displays.Count, service.Displays.Select(d => d.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (DisplayInfo display in service.Displays)
        {
            Assert.False(string.IsNullOrWhiteSpace(display.DeviceId));
            Assert.False(string.IsNullOrWhiteSpace(display.FriendlyName));
            Assert.True(display.Dpi >= 96);
            Assert.False(display.Bounds.IsEmpty);
            Assert.True(display.Bounds.Contains(display.WorkArea));
        }

        Assert.Equal(new PointI(0, 0), service.Primary.Bounds.TopLeft);
        Assert.Contains(service.FromCursor(), service.Displays);
        Assert.Contains(log.Entries, e => e.Message.Contains("display(s)", StringComparison.Ordinal));
        ui.Invoke(service.Dispose);
    }

    private sealed class FakeDisplaySource(IReadOnlyList<MonitorSample> samples) : IDisplaySource
    {
        private int identityReads;

        public volatile IReadOnlyList<MonitorSample> Samples = samples;

        public int IdentityReads => Volatile.Read(ref identityReads);

        public IReadOnlyList<MonitorSample> SampleMonitors() => Samples;

        public IReadOnlyDictionary<string, DisplayIdentity> ReadIdentities(IReadOnlyList<MonitorSample> monitors)
        {
            Interlocked.Increment(ref identityReads);
            return DisplayTopologyTests.ReferenceIdentities;
        }
    }
}
