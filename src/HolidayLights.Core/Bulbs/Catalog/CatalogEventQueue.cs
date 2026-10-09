using System.Threading.Channels;

namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// Delivers catalog change events in order on a thread-pool thread, so that the code that changes the catalog (indexing,
/// the folder watcher, UI calls) never waits for subscribers, and a caller that waits for indexing cannot deadlock
/// with a subscriber that marshals synchronously to its thread.
/// </summary>
internal sealed class CatalogEventQueue : IDisposable
{
    private const string LogSource = "Bulbs.Catalog";

    private readonly Channel<BulbCatalogChangedEventArgs> channel =
        Channel.CreateUnbounded<BulbCatalogChangedEventArgs>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Action<BulbCatalogChangedEventArgs> raise;
    private readonly IAppLog log;
    private readonly Task pump;

    /// <summary>Starts the queue.</summary>
    /// <param name="raise">Raises one event.</param>
    /// <param name="log">Receives subscriber failures.</param>
    public CatalogEventQueue(Action<BulbCatalogChangedEventArgs> raise, IAppLog log)
    {
        this.raise = raise;
        this.log = log;
        pump = Task.Run(PumpAsync);
    }

    /// <summary>Queues an event.</summary>
    /// <param name="change">What changed.</param>
    /// <param name="ids">The affected ids.</param>
    public void Post(BulbCatalogChange change, IReadOnlyList<string> ids) =>
        channel.Writer.TryWrite(new BulbCatalogChangedEventArgs(change, ids));

    /// <summary>Delivers the events already queued, then stops.</summary>
    public void Dispose()
    {
        channel.Writer.TryComplete();
        pump.Wait(TimeSpan.FromSeconds(2));
    }

    private async Task PumpAsync()
    {
        await foreach (BulbCatalogChangedEventArgs args in channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                raise(args);
            }
            catch (Exception e)
            {
                log.Error(LogSource, "A bulb catalog subscriber failed.", e);
            }
        }
    }
}
