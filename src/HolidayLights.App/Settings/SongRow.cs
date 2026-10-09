using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HolidayLights.App.Settings;

/// <summary>One row of the Music Box song list (PRODUCT-SPEC 3.4.3): check box, play button, Song, Length, Type, Arranger.</summary>
public sealed class SongRow : INotifyPropertyChanged
{
    private bool isChecked;
    private bool isPlaying;

    /// <summary>Creates a row.</summary>
    /// <param name="song">The song.</param>
    /// <param name="isChecked">True when the song is checked (not in the disabled list).</param>
    /// <param name="isPlaying">True while it plays.</param>
    public SongRow(SongInfo song, bool isChecked, bool isPlaying)
    {
        ArgumentNullException.ThrowIfNull(song);
        Song = song;
        this.isChecked = isChecked;
        this.isPlaying = isPlaying;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The song.</summary>
    public SongInfo Song { get; }

    /// <summary>The song id.</summary>
    public string Id => Song.Id;

    /// <summary>The title (file name without extension).</summary>
    public string Title => Song.Title;

    /// <summary>"1:58", or "" when unknown.</summary>
    public string LengthText => Song.Length is { } length ? MusicStatusText.Duration(length) : "";

    /// <summary>"MIDI", "MP3", "WAV", "WMA", "AIFF", "AU", "MPEG" or "M4A".</summary>
    public string TypeText => TypeName(Song.Kind);

    /// <summary>The arranger (bundled songs) or the artist tag.</summary>
    public string Arranger => Song.Arranger ?? "";

    /// <summary>True when Windows cannot decode the file (warning glyph).</summary>
    public bool IsUnplayable => !Song.IsPlayable;

    /// <summary>True when the song is checked.</summary>
    public bool IsChecked
    {
        get => isChecked;
        set => Set(ref isChecked, value);
    }

    /// <summary>True while the song plays (the equalizer glyph replaces the play button).</summary>
    public bool IsPlaying
    {
        get => isPlaying;
        set
        {
            if (Set(ref isPlaying, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNotPlaying)));
            }
        }
    }

    /// <summary>The opposite of <see cref="IsPlaying"/> (the play button shows).</summary>
    public bool IsNotPlaying => !isPlaying;

    /// <summary>"Jingle Bells, checked, 2:13, MIDI, George Larson, playing".</summary>
    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { Title, IsChecked ? "checked" : "not checked" };
            if (LengthText.Length > 0)
            {
                parts.Add(LengthText);
            }

            parts.Add(TypeText);
            if (Arranger.Length > 0)
            {
                parts.Add(Arranger);
            }

            if (IsPlaying)
            {
                parts.Add("playing");
            }

            if (IsUnplayable)
            {
                parts.Add("can't be played");
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>The "Type" column of a song kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The type name.</returns>
    public static string TypeName(SongKind kind) => kind switch
    {
        SongKind.Midi => "MIDI",
        SongKind.Mp3 => "MP3",
        SongKind.Wav => "WAV",
        SongKind.Wma => "WMA",
        SongKind.Aiff => "AIFF",
        SongKind.Au => "AU",
        SongKind.Mpeg => "MPEG",
        _ => "M4A",
    };

    /// <inheritdoc />
    public override string ToString() => Title;

    private bool Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
        return true;
    }
}
