using System.IO.Pipes;
using System.Text;
using HolidayLights.Platform.Instance;

namespace HolidayLights.Tests.Platform;

/// <summary>Line framing of the instance pipe, over an anonymous pipe pair (raw bytes in, messages out).</summary>
public sealed class PipeConnectionTests
{
    private static readonly string AcceptedLine = new InstanceMessage { Accepted = true }.ToLine();

    [Fact]
    public async Task Lines_AreParsedAndBadOnesSkipped()
    {
        string lines =
            new InstanceMessage { Command = InstanceCommand.ShowSettings(SettingsPageId.ScreenSaver) }.ToLine() + "\r\n" +
            "this is not json\n" +
            "\n" +
            "{\"command\":{\"kind\":\"no-such-command\"}}\n" +
            new InstanceMessage { Command = InstanceCommand.Lights(false) }.ToLine() + "\n" +
            AcceptedLine;

        List<InstanceMessage> messages = await ReadAllAsync(Encoding.UTF8.GetBytes(lines));

        Assert.Equal(3, messages.Count);
        Assert.Equal(InstanceCommandKind.ShowSettings, messages[0].Command!.Kind);
        Assert.Equal(["saver"], messages[0].Command!.Arguments);
        Assert.Equal(["off"], messages[1].Command!.Arguments);
        Assert.True(messages[2].Accepted);
    }

    [Fact]
    public async Task AnOverlongLine_IsDroppedAndTheNextOneStillCounts()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes("{\"accepted\":true,\"padding\":\""));
        bytes.AddRange(Enumerable.Repeat((byte)'x', PipeConnection.MaxLineBytes + 10));
        bytes.AddRange(Encoding.UTF8.GetBytes("\"}\n" + AcceptedLine + "\n"));

        InstanceMessage message = Assert.Single(await ReadAllAsync([.. bytes]));

        Assert.True(message.Accepted);
    }

    [Fact]
    public async Task MultiByteText_SplitAcrossReadsSurvives()
    {
        string path = "C:\\Bulbs\\Schneemann – Größe ✓.bul";
        string line = new InstanceMessage { Command = InstanceCommand.Open([path]) }.ToLine() + "\n";

        // Small writes so that characters are split between reads.
        List<InstanceMessage> messages = await ReadAllAsync(Encoding.UTF8.GetBytes(line), chunkSize: 3);

        Assert.Equal([path], Assert.Single(messages).Command!.Arguments);
    }

    [Fact]
    public async Task ASecondReader_ContinuesWhereTheFirstStopped()
    {
        using var server = new AnonymousPipeServerStream(PipeDirection.In);
        await using (var client = new AnonymousPipeClientStream(PipeDirection.Out, server.ClientSafePipeHandle))
        {
            // Both lines arrive in one read.
            byte[] both = Encoding.UTF8.GetBytes(
                new InstanceMessage { Command = InstanceCommand.Simple(InstanceCommandKind.SaverStarted) }.ToLine() + "\n" + AcceptedLine + "\n");
            await client.WriteAsync(both);
        }

        await using var connection = new PipeConnection(server);
        InstanceMessage? first = null;
        await foreach (InstanceMessage message in connection.ReadAllAsync())
        {
            first = message;
            break;
        }

        var rest = new List<InstanceMessage>();
        await foreach (InstanceMessage message in connection.ReadAllAsync())
        {
            rest.Add(message);
        }

        Assert.Equal(InstanceCommandKind.SaverStarted, first!.Command!.Kind);
        Assert.True(Assert.Single(rest).Accepted);
    }

    [Fact]
    public async Task SendAsync_WritesOneLinePerMessage()
    {
        using var server = new AnonymousPipeServerStream(PipeDirection.In);
        using var client = new AnonymousPipeClientStream(PipeDirection.Out, server.ClientSafePipeHandle);
        await using (var connection = new PipeConnection(client))
        {
            await connection.SendAsync(new InstanceMessage { Accepted = true });
            await connection.SendAsync(new InstanceMessage { Command = InstanceCommand.Simple(InstanceCommandKind.Exit) });
        }

        string text = await new StreamReader(server, Encoding.UTF8).ReadToEndAsync();

        Assert.Equal(AcceptedLine + "\n" + new InstanceMessage { Command = InstanceCommand.Simple(InstanceCommandKind.Exit) }.ToLine() + "\n", text);
    }

    private static async Task<List<InstanceMessage>> ReadAllAsync(byte[] bytes, int chunkSize = 64 * 1024)
    {
        using var server = new AnonymousPipeServerStream(PipeDirection.In);
        var client = new AnonymousPipeClientStream(PipeDirection.Out, server.ClientSafePipeHandle);
        Task writer = Task.Run(async () =>
        {
            await using (client)
            {
                for (int offset = 0; offset < bytes.Length; offset += chunkSize)
                {
                    await client.WriteAsync(bytes.AsMemory(offset, Math.Min(chunkSize, bytes.Length - offset)));
                    await client.FlushAsync();
                }
            }
        });

        var messages = new List<InstanceMessage>();
        await using var connection = new PipeConnection(server);
        await foreach (InstanceMessage message in connection.ReadAllAsync())
        {
            messages.Add(message);
        }

        await writer;
        return messages;
    }
}
