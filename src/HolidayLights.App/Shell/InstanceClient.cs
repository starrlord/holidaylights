using System.IO;

namespace HolidayLights.App.Shell;

/// <summary>The client side of the instance pipe: a later launch forwards its command and waits for the answer (PRODUCT-SPEC 6.6.2).</summary>
public static class InstanceClient
{
    /// <summary>How long a later launch waits for the running instance to connect and to answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Sends a command to the running instance and waits for its answer.</summary>
    /// <param name="instance">The single-instance object (not claimed by this process).</param>
    /// <param name="command">The command.</param>
    /// <param name="log">The log.</param>
    /// <returns>True when the running instance accepted the command.</returns>
    public static async Task<bool> ForwardAsync(ISingleInstance instance, InstanceCommand command, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(log);
        if (LaunchCommands.ShowsWindow(command))
        {
            // This process received the user's click or key press: pass the right to take the foreground on.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.AllowAnyProcess);
        }

        try
        {
            await using IInstanceConnection? connection = await instance.ConnectAsync(Timeout).ConfigureAwait(false);
            if (connection is null)
            {
                log.Warn("Shell.Instance", "Holiday Lights is running but did not answer.");
                return false;
            }

            await connection.SendAsync(new InstanceMessage { Command = command }).ConfigureAwait(false);
            using var answer = new CancellationTokenSource(Timeout);
            await foreach (InstanceMessage message in connection.ReadAllAsync(answer.Token).ConfigureAwait(false))
            {
                if (message.Accepted is { } accepted)
                {
                    log.Info("Shell.Instance", $"Forwarded {command}: {(accepted ? "accepted" : "refused")}.");
                    return accepted;
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            log.Warn("Shell.Instance", $"Forwarding {command} failed.", e);
        }

        return false;
    }
}
