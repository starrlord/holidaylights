using HolidayLights.Audio;

namespace HolidayLights.App.Shell;

/// <summary>
/// The music policy of a settings-only session (<c>/c</c>, legacy <c>settings</c>; PRODUCT-SPEC 2.5.4): that session plays
/// no music of its own, but a song started from the Music Box ("Play Now", "Next Song") follows Volume, Mute, MIDI Output
/// and the checked songs, and changes to them reach the song at once (<see cref="MusicPolicyRules.ForSettingsOnly"/>).
/// Created with the music director of the session. UI thread.
/// </summary>
internal sealed class SettingsOnlyMusic : IDisposable
{
    private readonly ISettingsStore settings;
    private readonly IMusicDirector music;
    private MusicPolicy? applied;
    private bool disposed;

    /// <summary>Applies the policy of the current settings and follows their changes.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="music">The music director of the session.</param>
    public SettingsOnlyMusic(ISettingsStore settings, IMusicDirector music)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(music);
        this.settings = settings;
        this.music = music;
        settings.Changed += OnSettingsChanged;
        Apply();
    }

    /// <summary>Stops following the settings.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Music != e.NewSettings.Music || e.OldSettings.Current.Music != e.NewSettings.Current.Music)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (disposed)
        {
            return;
        }

        MusicPolicy policy = MusicPolicyRules.ForSettingsOnly(settings.Current);
        if (applied is not null && MusicPolicyRules.AreEquivalent(applied, policy))
        {
            return;
        }

        applied = policy;
        music.ApplyPolicy(policy);
    }
}
