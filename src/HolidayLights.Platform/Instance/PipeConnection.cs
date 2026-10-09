using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

namespace HolidayLights.Platform.Instance;

/// <summary>
/// One end of the instance pipe: UTF-8 JSON <see cref="InstanceMessage"/> lines terminated by <c>\n</c>. Writes are
/// serialized (a screen saver connection receives music events and acknowledgements); invalid or over-long lines are
/// skipped.
/// </summary>
/// <remarks>
/// One reader at a time: bytes received after the message a reader stopped at are kept, so a later
/// <see cref="ReadAllAsync"/> continues where the previous one ended.
/// </remarks>
internal sealed class PipeConnection : IInstanceConnection
{
    /// <summary>The longest line accepted (an <c>open</c> command with hundreds of paths fits easily).</summary>
    internal const int MaxLineBytes = 1024 * 1024;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly PipeStream pipe;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly byte[] received = new byte[4096];
    private readonly MemoryStream line = new();
    private int receivedStart;
    private int receivedEnd;
    private bool overlong;
    private bool ended;
    private int disposed;

    /// <summary>Wraps a connected pipe; the connection owns it.</summary>
    /// <param name="pipe">The connected pipe.</param>
    public PipeConnection(PipeStream pipe) => this.pipe = pipe;

    /// <inheritdoc />
    public async ValueTask SendAsync(InstanceMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        byte[] bytes = Utf8.GetBytes(message.ToLine() + "\n");
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<InstanceMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            while (receivedStart < receivedEnd)
            {
                int newline = Array.IndexOf(received, (byte)'\n', receivedStart, receivedEnd - receivedStart);
                int end = newline < 0 ? receivedEnd : newline;
                Append(received.AsSpan(receivedStart, end - receivedStart));
                receivedStart = newline < 0 ? receivedEnd : newline + 1;
                if (newline >= 0 && TakeMessage() is { } message)
                {
                    yield return message;
                }
            }

            if (ended)
            {
                yield break;
            }

            int read = await ReadChunkAsync(cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                // The other side closed: a last line without a terminator still counts.
                ended = true;
                if (TakeMessage() is { } last)
                {
                    yield return last;
                }

                yield break;
            }

            receivedStart = 0;
            receivedEnd = read;
        }
    }

    /// <summary>Closes the pipe at once (a reader ends; pending writes fail).</summary>
    public void Abort()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            pipe.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Abort();
        return ValueTask.CompletedTask;
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        if (overlong)
        {
            return;
        }

        if (line.Length + bytes.Length > MaxLineBytes)
        {
            // Too long to be ours: drop it up to the next line break.
            overlong = true;
            line.SetLength(0);
            return;
        }

        line.Write(bytes);
    }

    private InstanceMessage? TakeMessage()
    {
        bool skip = overlong || line.Length == 0;
        string text = skip ? "" : Utf8.GetString(line.GetBuffer(), 0, (int)line.Length);
        line.SetLength(0);
        overlong = false;
        return !skip && InstanceMessage.TryParseLine(text.TrimEnd('\r'), out InstanceMessage? message) ? message : null;
    }

    private async ValueTask<int> ReadChunkAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await pipe.ReadAsync(received, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException ||
                                  (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A broken or closed pipe (also a read aborted by Abort) ends the stream like a clean close.
            return 0;
        }
    }
}
