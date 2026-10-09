namespace HolidayLights.Audio.Playback;

/// <summary>Facts about the audio output that decide the Music Box problems of PRODUCT-SPEC 3.4.6.</summary>
internal interface IAudioEnvironment
{
    /// <summary>True when a speaker or headphone output exists ("No speakers or headphones are available" otherwise).</summary>
    /// <returns>True when music can be heard; true as well when it cannot be determined.</returns>
    bool HasOutputDevice();

    /// <summary>Whether Holiday Lights is muted or at 0 in the Windows volume mixer (its WASAPI session).</summary>
    /// <returns>True or false, or null when it cannot be determined (no session yet).</returns>
    bool? IsMutedInMixer();
}
