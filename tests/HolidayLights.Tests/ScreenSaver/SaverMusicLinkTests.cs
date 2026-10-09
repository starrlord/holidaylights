using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HolidayLights.App.ScreenSaver.Music;
using HolidayLights.Audio;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class SaverMusicLinkTests
{
    private static readonly AppSettings Settings = new()
    {
        Music = new MusicSettings { Enabled = true, Volume = 35, Muted = false, MidiDevice = "GS Wavetable", SyncOffsetMs = 55 },
        Current = new ThemeableSettings { Music = new CurrentMusic { Mode = PlayMode.SaverOn, DisabledSongs = ["bundled:Danny Boy.mid"] } },
    };

    [Fact]
    public async Task WhenHolidayLightsRuns_TheSaverAnnouncesItself_AndDancesToItsMusic()
    {
        var connection = new FakeConnection();
        var instance = new FakeInstance(connection);
        var music = new FakeDirector();

        SaverMusicLink link = await SaverMusicLink.StartAsync(() => instance, music, Settings, false, new RecordingLog());

        Assert.True(link.IsConnected);
        Assert.Equal(InstanceCommandKind.SaverStarted, Assert.Single(connection.Sent).Command!.Kind);
        Assert.Empty(music.Policies);
        using IMusicEventReader reader = link.Events.Subscribe();
        var note = new MusicEvent(MusicEventKind.NoteOn, 12345, 0, 60, 100);
        await connection.DeliverAsync(new InstanceMessage { Music = note });
        Assert.Equal(note, await ReadOneAsync(reader));

        await link.DisposeAsync();

        Assert.Equal(InstanceCommandKind.SaverStopped, connection.Sent[^1].Command!.Kind);
        Assert.True(connection.Disposed);
        Assert.True(instance.Disposed);
        Assert.Null(music.StoppedWith);
    }

    [Fact]
    public async Task WithoutHolidayLights_TheSaverPlaysItsOwnMusic_AndStopsItAtTheEnd()
    {
        var music = new FakeDirector();

        SaverMusicLink link = await SaverMusicLink.StartAsync(() => null, music, Settings, false, new RecordingLog());

        Assert.False(link.IsConnected);
        Assert.Same(music.Events, link.Events);
        MusicPolicy policy = Assert.Single(music.Policies);
        Assert.True(policy.SaverRunning);
        Assert.True(policy.Enabled);
        Assert.Equal(PlayMode.SaverOn, policy.Mode);
        Assert.Equal(["bundled:Danny Boy.mid"], policy.DisabledSongs);
        Assert.Equal((35, false, "GS Wavetable", 55), (policy.Volume, policy.Muted, policy.MidiDevice, policy.SyncOffsetMs));
        Assert.False(policy.Stopped);

        await link.DisposeAsync();

        Assert.Equal(SaverMusicLink.FadeOut, music.StoppedWith);
    }

    [Fact]
    public async Task WhenHolidayLightsDoesNotAnswer_TheSaverPlaysItsOwnMusic()
    {
        var instance = new FakeInstance(null);
        var music = new FakeDirector();

        SaverMusicLink link = await SaverMusicLink.StartAsync(() => instance, music, Settings, true, new RecordingLog());

        Assert.False(link.IsConnected);
        Assert.True(instance.Disposed);
        Assert.True(Assert.Single(music.Policies).Stopped);
        await link.DisposeAsync();
    }

    private static async Task<MusicEvent> ReadOneAsync(IMusicEventReader reader)
    {
        var buffer = new MusicEvent[4];
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (reader.Read(buffer) > 0)
            {
                return buffer[0];
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("No music event arrived.");
    }

    private sealed class FakeInstance(IInstanceConnection? connection) : ISingleInstance
    {
        public bool Disposed { get; private set; }

        public bool TryClaim() => throw new InvalidOperationException("A saver never claims the instance.");

        public void Listen(Func<IInstanceConnection, Task> onConnection) => throw new InvalidOperationException("A saver never listens.");

        public Task<IInstanceConnection?> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(connection);

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeConnection : IInstanceConnection
    {
        private readonly Channel<InstanceMessage> incoming = Channel.CreateUnbounded<InstanceMessage>();

        public List<InstanceMessage> Sent { get; } = [];

        public bool Disposed { get; private set; }

        public ValueTask SendAsync(InstanceMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<InstanceMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (InstanceMessage message in incoming.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }
        }

        public ValueTask DeliverAsync(InstanceMessage message) => incoming.Writer.WriteAsync(message);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
