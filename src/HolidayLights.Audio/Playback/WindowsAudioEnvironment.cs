using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// Reads the default render endpoint and this process's audio sessions through Core Audio (NAudio.Wasapi). Read-only:
/// the Windows mixer is never changed. Call from an MTA thread (the director's music thread).
/// </summary>
internal sealed class WindowsAudioEnvironment : IAudioEnvironment
{
    private const float SilentVolume = 0.001f;

    /// <inheritdoc />
    public bool HasOutputDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (COMException)
        {
            return true;
        }
    }

    /// <inheritdoc />
    public bool? IsMutedInMixer()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out MMDevice? device) || device is null)
            {
                return null;
            }

            using (device)
            {
                return IsProcessMuted(device.AudioSessionManager, (uint)Environment.ProcessId);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>True when every session of the process is muted or at 0; null when the process has no session.</summary>
    private static bool? IsProcessMuted(AudioSessionManager manager, uint processId)
    {
        manager.RefreshSessions();
        using SessionCollection sessions = manager.Sessions;
        bool? muted = null;
        for (int i = 0; i < sessions.Count; i++)
        {
            using AudioSessionControl session = sessions[i];
            if (session.GetProcessID != processId)
            {
                continue;
            }

            SimpleAudioVolume volume = session.SimpleAudioVolume;
            if (!volume.Mute && volume.Volume > SilentVolume)
            {
                return false;
            }

            muted = true;
        }

        return muted;
    }
}
