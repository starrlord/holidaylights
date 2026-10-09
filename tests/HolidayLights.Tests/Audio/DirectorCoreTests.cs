using System.Globalization;
using System.Text.Json;
using HolidayLights.Audio;
using HolidayLights.Audio.Events;
using HolidayLights.Audio.Midi;
using HolidayLights.Audio.Scheduling;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Audio;

/// <summary>
/// The Music Box decisions on a fake clock (PRODUCT-SPEC 6.1): the five play modes, the gaps, the shuffle
/// bag in use, and the 6.0 rules (switch, hold, pause rules, failures, synthesizer, output device, transport, exit).
/// </summary>
public sealed class DirectorCoreTests
{
    [Theory]
    [InlineData(false, PlayMode.Always, false, MusicStatus.Off, false)]
    [InlineData(false, PlayMode.Intermittently, true, MusicStatus.Off, false)]
    [InlineData(true, PlayMode.Never, false, MusicStatus.NeverMode, false)]
    [InlineData(true, PlayMode.Never, true, MusicStatus.NeverMode, false)]
    [InlineData(true, PlayMode.SaverOn, false, MusicStatus.WaitingForScreenSaver, false)]
    [InlineData(true, PlayMode.SaverOn, true, MusicStatus.Playing, true)]
    [InlineData(true, PlayMode.SaverOff, false, MusicStatus.Playing, true)]
    [InlineData(true, PlayMode.SaverOff, true, MusicStatus.WaitingForScreenSaverToEnd, false)]
    [InlineData(true, PlayMode.Always, false, MusicStatus.Playing, true)]
    [InlineData(true, PlayMode.Always, true, MusicStatus.Playing, true)]
    [InlineData(true, PlayMode.Intermittently, false, MusicStatus.Playing, true)]
    [InlineData(true, PlayMode.Intermittently, true, MusicStatus.Playing, true)]
    public void Play_modes_decide_whether_music_plays(bool enabled, PlayMode mode, bool saverRunning, MusicStatus status, bool plays)
    {
        var h = new Harness();

        h.Core.ApplyPolicy(new MusicPolicy { Enabled = enabled, Mode = mode, SaverRunning = saverRunning });

        Assert.Equal(status, h.State.Status);
        Assert.Equal(plays ? 1 : 0, h.Players.Count);
    }

    [Fact]
    public void Always_starts_at_once_and_waits_1_second_between_songs()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer first = h.Players.Last;

        first.Finish();

        MusicState gap = h.State;
        Assert.Equal(MusicStatus.BetweenSongs, gap.Status);
        Assert.Equal(h.Time.GetUtcNow() + TimeSpan.FromSeconds(1), gap.NextSongAt);
        Assert.Equal(h.Core.Now + TimeSpan.FromSeconds(1), h.Core.NextWakeUp);
        h.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, h.Players.Count);
        h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, h.Players.Count);
        Assert.Equal(MusicStatus.Playing, h.State.Status);
        Assert.True(first.IsDisposed);
    }

    [Fact]
    public void Intermittently_waits_60_seconds_plus_a_random_part_up_to_2_minutes()
    {
        var h = new Harness(gapDraws: [45_000]);
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));

        h.Players.Last.Finish();

        Assert.Equal([120_000], h.GapRandom.Requests);
        Assert.Equal(h.Time.GetUtcNow() + TimeSpan.FromSeconds(105), h.State.NextSongAt);
        h.Advance(TimeSpan.FromSeconds(105) - TimeSpan.FromTicks(1));
        Assert.Equal(1, h.Players.Count);
        h.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void An_intermittent_pause_is_honoured_by_every_reevaluation()
    {
        var h = new Harness(gapDraws: [0]);
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));
        h.Players.Last.Finish();

        h.Advance(TimeSpan.FromSeconds(10));
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently) with { Volume = 40 });
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently) with { Volume = 41, DisabledSongs = Disabled(h.Songs[4].Id) });

        Assert.Equal(1, h.Players.Count);
        Assert.Equal(MusicStatus.BetweenSongs, h.State.Status);
        h.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void A_wake_up_before_anything_is_due_changes_nothing()
    {
        var h = new Harness(gapDraws: [0]);
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));
        h.Players.Last.Finish();

        h.Time.Advance(TimeSpan.FromSeconds(30));
        h.Core.OnWakeUp();
        h.Core.OnWakeUp();

        Assert.Equal(1, h.Players.Count);
        Assert.Equal(h.Core.Now + TimeSpan.FromSeconds(30), h.Core.NextWakeUp);
    }

    [Fact]
    public void The_gap_follows_a_mode_change_measured_from_the_end_of_the_song()
    {
        var h = new Harness(gapDraws: [100_000]);
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));
        h.Players.Last.Finish();
        h.Advance(TimeSpan.FromSeconds(3));

        h.Core.ApplyPolicy(Harness.On(PlayMode.Always));

        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void The_director_draws_the_shuffle_exactly_like_5_4()
    {
        int[] expected = GoldenSequence(songCount: 15, seed: 1);
        var h = new Harness(songs: 15, shuffle: new MsvcRandom(1));
        h.Core.ApplyPolicy(Harness.On());
        var played = new List<int>();

        for (int i = 0; i < expected.Length; i++)
        {
            FakeSongPlayer player = h.Players.Last;
            played.Add(h.Songs.ToList().FindIndex(s => s.Id == player.Song.Id));
            player.Finish();
            h.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(expected, played);
    }

    [Fact]
    public void Only_checked_playable_songs_are_chosen()
    {
        var songs = new[] { TestSongs.Bundled("A"), TestSongs.Bundled("B", playable: false), TestSongs.Bundled("C") };
        var h = new Harness(songs);

        h.Core.ApplyPolicy(Harness.On() with { DisabledSongs = Disabled(songs[0].Id) });
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(songs[2].Id, h.Players.Last.Song.Id);
            h.Players.Last.Finish();
            h.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void No_checked_songs_means_no_music()
    {
        var h = new Harness(songs: 2);

        h.Core.ApplyPolicy(Harness.On() with { DisabledSongs = Disabled(h.Songs[0].Id, h.Songs[1].Id) });

        Assert.Equal(MusicStatus.NoSongs, h.State.Status);
        h.Core.ApplyPolicy(Harness.On() with { DisabledSongs = Disabled(h.Songs[0].Id) });
        Assert.Equal(h.Songs[1].Id, h.Players.Last.Song.Id);
    }

    [Fact]
    public void Unchecking_the_playing_song_stops_it_and_starts_another_at_once()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;

        h.Core.ApplyPolicy(Harness.On() with { DisabledSongs = Disabled(playing.Song.Id) });

        Assert.True(playing.IsStopped);
        Assert.Equal(2, h.Players.Count);
        Assert.NotEqual(playing.Song.Id, h.Players.Last.Song.Id);
    }

    /// <summary>Review r1 #35: a user pause ends with its song, so the music goes on with the next one.</summary>
    [Fact]
    public void Unchecking_the_paused_song_ends_the_pause_and_starts_another()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;
        h.Core.Pause();
        Assert.Equal(MusicStatus.Paused, h.State.Status);

        h.Core.ApplyPolicy(Harness.On() with { DisabledSongs = Disabled(playing.Song.Id) });

        Assert.True(playing.IsStopped);
        Assert.Equal(2, h.Players.Count);
        Assert.False(h.Players.Last.IsPaused);
        Assert.Equal(MusicStatus.Playing, h.State.Status);
    }

    [Fact]
    public void Turning_the_music_off_and_on_ends_the_users_pause()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        h.Core.Pause();

        h.Core.ApplyPolicy(Harness.On() with { Enabled = false });
        h.Core.ApplyPolicy(Harness.On());

        Assert.NotEqual(MusicStatus.Paused, h.State.Status);
        Assert.Equal(2, h.Players.Count);
        Assert.False(h.Players.Last.IsPaused);
    }

    [Fact]
    public void A_still_allowed_song_keeps_playing_when_settings_change()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;
        string other = h.Songs.First(s => s.Id != playing.Song.Id).Id;

        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently) with { DisabledSongs = Disabled(other) });
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently) with { DisabledSongs = Disabled(other), SaverRunning = true });

        Assert.Equal(1, h.Players.Count);
        Assert.False(playing.IsStopped);
    }

    [Fact]
    public void Only_when_the_screen_saver_is_on_follows_the_saver()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On(PlayMode.SaverOn));
        Assert.Equal(0, h.Players.Count);

        h.Core.ApplyPolicy(Harness.On(PlayMode.SaverOn) with { SaverRunning = true });
        FakeSongPlayer playing = h.Players.Last;
        h.Core.ApplyPolicy(Harness.On(PlayMode.SaverOn));

        Assert.True(playing.IsStopped);
        Assert.Equal(MusicStatus.WaitingForScreenSaver, h.State.Status);
    }

    [Fact]
    public void The_first_song_after_start_fades_in_over_3_seconds()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On() with { Volume = 60 });

        Assert.Equal(TimeSpan.FromSeconds(3), h.Players.Last.FadeIn);
        Assert.Equal(0.6, h.Players.Last.Volume, 6);
        h.Players.Last.Finish();
        h.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, h.Players.Last.FadeIn);
    }

    [Fact]
    public void Volume_and_mute_reach_the_playing_song_at_once()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On() with { Volume = 60 });

        h.Core.ApplyPolicy(Harness.On() with { Volume = 25 });
        Assert.Equal(0.25, h.Players.Last.Volume, 6);
        h.Core.ApplyPolicy(Harness.On() with { Volume = 25, Muted = true });
        Assert.Equal(0, h.Players.Last.Volume);
        Assert.Equal(1, h.Players.Count);
    }

    [Fact]
    public void Turning_the_switch_off_stops_even_a_play_now_song_and_on_starts_at_once()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(new MusicPolicy { Enabled = false });
        h.Core.PlayNow(h.Songs[2].Id);
        FakeSongPlayer requested = h.Players.Last;

        h.Core.ApplyPolicy(new MusicPolicy { Enabled = false, Volume = 30 });
        Assert.False(requested.IsStopped);
        h.Core.ApplyPolicy(Harness.On());
        h.Core.ApplyPolicy(new MusicPolicy { Enabled = false });

        Assert.True(requested.IsStopped);
        Assert.Equal(MusicStatus.Off, h.State.Status);
        h.Core.ApplyPolicy(Harness.On());
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void Play_now_ignores_mode_and_check_boxes_and_leaves_the_bag_alone()
    {
        var reference = new Harness(songs: 6, shuffle: new MsvcRandom(5));
        reference.Core.ApplyPolicy(Harness.On());
        string expectedNext = reference.Players.Last.Song.Id;

        var h = new Harness(songs: 6, shuffle: new MsvcRandom(5));
        h.Core.ApplyPolicy(Harness.On(PlayMode.Never) with { DisabledSongs = Disabled(h.Songs[3].Id) });
        h.Core.PlayNow(h.Songs[3].Id);
        Assert.Equal(h.Songs[3].Id, h.Players.Last.Song.Id);
        Assert.Equal(TimeSpan.Zero, h.Players.Last.FadeIn);
        h.Players.Last.Finish();
        h.Core.ApplyPolicy(Harness.On());

        Assert.Equal(expectedNext, h.Players.Last.Song.Id);
    }

    [Fact]
    public void Next_song_plays_another_song_at_once_even_during_a_gap()
    {
        var h = new Harness(gapDraws: [90_000]);
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));
        FakeSongPlayer first = h.Players.Last;

        h.Core.NextSong();
        Assert.True(first.IsStopped);
        Assert.NotEqual(first.Song.Id, h.Players.Last.Song.Id);
        h.Players.Last.Finish();
        h.Core.NextSong();

        Assert.Equal(3, h.Players.Count);
        Assert.Equal(MusicStatus.Playing, h.State.Status);
    }

    [Fact]
    public void Previous_restarts_the_song_and_within_3_seconds_goes_back_one()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        string first = h.Players.Last.Song.Id;
        h.Core.NextSong();
        string second = h.Players.Last.Song.Id;

        h.Core.Previous();
        Assert.Equal(second, h.Players.Last.Song.Id);
        h.Advance(TimeSpan.FromSeconds(2));
        h.Core.Previous();
        Assert.Equal(first, h.Players.Last.Song.Id);
        h.Advance(TimeSpan.FromSeconds(4));
        h.Core.Previous();
        Assert.Equal(first, h.Players.Last.Song.Id);
        Assert.Equal(5, h.Players.Count);
    }

    [Fact]
    public void Pause_rules_pause_mid_song_and_resume_where_it_was()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;

        h.Core.ApplyPolicy(Harness.On() with { PausedByRules = true });
        Assert.True(playing.IsPaused);
        Assert.Equal(MusicStatus.PausedByRules, h.State.Status);
        Assert.Equal(playing.Song, h.State.CurrentSong);

        h.Core.ApplyPolicy(Harness.On());
        Assert.False(playing.IsPaused);
        Assert.Equal(1, h.Players.Count);
        Assert.Equal([MusicEventKind.SongStarted, MusicEventKind.Paused, MusicEventKind.Resumed], h.EventKinds());
    }

    [Fact]
    public void No_song_starts_while_paused_and_the_due_song_starts_when_the_pause_ends()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        h.Players.Last.Finish();
        h.Core.ApplyPolicy(Harness.On() with { PausedByRules = true });

        h.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, h.Players.Count);
        Assert.Equal(MusicStatus.PausedByRules, h.State.Status);
        Assert.Null(h.Core.NextWakeUp);

        h.Core.ApplyPolicy(Harness.On());
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void The_users_pause_lasts_until_resume()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;

        h.Core.Pause();
        h.Core.ApplyPolicy(Harness.On() with { Volume = 10 });
        Assert.True(playing.IsPaused);
        Assert.Equal(MusicStatus.Paused, h.State.Status);
        h.Core.Resume();

        Assert.False(playing.IsPaused);
        Assert.Equal(MusicStatus.Playing, h.State.Status);
    }

    [Fact]
    public void Remote_desktop_stops_the_music_and_ignores_commands_until_it_ends()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;

        h.Core.ApplyPolicy(Harness.On() with { Stopped = true });
        h.Core.PlayNow(h.Songs[0].Id);
        h.Core.NextSong();

        Assert.True(playing.IsStopped);
        Assert.Equal(1, h.Players.Count);
        Assert.Equal(MusicStatus.Stopped, h.State.Status);
        h.Core.ApplyPolicy(Harness.On());
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void The_first_song_waits_for_the_welcome_card_then_fades_in()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On() with { HoldFirstSong = true });
        Assert.Equal(MusicStatus.Held, h.State.Status);
        Assert.Equal(0, h.Players.Count);

        h.Core.ApplyPolicy(Harness.On());

        Assert.Equal(TimeSpan.FromSeconds(3), h.Players.Last.FadeIn);
    }

    [Fact]
    public void A_failed_song_is_skipped_after_1_second_and_3_failures_stop_the_music()
    {
        var h = new Harness();
        h.Players.OpenError = _ => new InvalidDataException("damaged");
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));

        Assert.Equal(MusicStatus.BetweenSongs, h.State.Status);
        Assert.Equal(h.Time.GetUtcNow() + TimeSpan.FromSeconds(1), h.State.NextSongAt);
        h.Advance(TimeSpan.FromSeconds(1));
        h.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(MusicStatus.StoppedAfterFailures, h.State.Status);
        Assert.Null(h.Core.NextWakeUp);

        h.Players.OpenError = null;
        h.Core.RetryNow();
        Assert.Equal(MusicStatus.Playing, h.State.Status);
    }

    [Fact]
    public void A_song_failing_mid_way_skips_to_the_next_after_1_second()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));

        h.Players.Last.Fail(new MidiOutputException(6, "gone"));

        Assert.Equal(h.Core.Now + TimeSpan.FromSeconds(1), h.Core.NextWakeUp);
        h.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void A_busy_synthesizer_is_tried_again_every_30_seconds()
    {
        var h = new Harness();
        h.Players.OpenError = _ => new MidiOutputException(4, "busy");
        h.Core.ApplyPolicy(Harness.On());

        Assert.Equal(MusicStatus.WaitingForSynthesizer, h.State.Status);
        Assert.Equal(h.Core.Now + TimeSpan.FromSeconds(30), h.Core.NextWakeUp);
        h.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(MusicStatus.WaitingForSynthesizer, h.State.Status);

        h.Players.OpenError = null;
        h.Core.RetryNow();
        Assert.Equal(MusicStatus.Playing, h.State.Status);
    }

    [Fact]
    public void Changing_the_midi_output_moves_the_playing_midi_song_to_the_new_device_at_once()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer first = h.Players.Last;
        h.EventKinds();

        h.Core.ApplyPolicy(Harness.On() with { MidiDevice = "Some Other Synth" });

        Assert.Equal(2, h.Players.Count);
        FakeSongPlayer moved = h.Players.Last;
        Assert.Equal(first.Song.Id, moved.Song.Id);
        Assert.Equal(("", "Some Other Synth"), (first.Context.MidiDevice, moved.Context.MidiDevice));
        Assert.True(first.IsStopped && first.IsDisposed);
        Assert.Equal(TimeSpan.Zero, moved.FadeIn);
        Assert.Equal(MusicStatus.Playing, h.State.Status);
        Assert.Equal(first.Song.Id, h.State.CurrentSong?.Id);
        Assert.Equal([MusicEventKind.SongStopped, MusicEventKind.SongStarted], h.EventKinds());

        // Settings that leave the device alone keep the song playing.
        h.Core.ApplyPolicy(Harness.On() with { MidiDevice = "Some Other Synth", Volume = 30 });
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void A_paused_or_requested_midi_song_keeps_its_state_on_the_new_device()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On(PlayMode.Never));
        h.Core.PlayNow(h.Songs[2].Id);
        h.Core.Pause();

        h.Core.ApplyPolicy(Harness.On(PlayMode.Never) with { MidiDevice = "Some Other Synth" });

        FakeSongPlayer moved = h.Players.Last;
        Assert.Equal((2, h.Songs[2].Id, "Some Other Synth"), (h.Players.Count, moved.Song.Id, moved.Context.MidiDevice));
        Assert.True(moved.IsPaused);
        Assert.Equal(MusicStatus.Paused, h.State.Status);
        h.Core.Resume();
        Assert.False(moved.IsPaused);
        Assert.Equal(MusicStatus.Playing, h.State.Status);
    }

    [Fact]
    public void Changing_the_midi_output_leaves_an_audio_file_playing()
    {
        var h = new Harness([TestSongs.Bundled("Carol", SongKind.Mp3)]);
        h.Core.ApplyPolicy(Harness.On());

        h.Core.ApplyPolicy(Harness.On() with { MidiDevice = "Some Other Synth" });

        Assert.Equal(1, h.Players.Count);
        Assert.False(h.Players.Last.IsStopped);
    }

    [Fact]
    public void Changing_the_midi_output_tries_a_song_waiting_for_a_busy_synthesizer_at_once()
    {
        var h = new Harness();
        h.Players.OpenError = _ => new MidiOutputException(4, "busy");
        h.Core.ApplyPolicy(Harness.On());
        Assert.Equal(MusicStatus.WaitingForSynthesizer, h.State.Status);

        h.Players.OpenError = null;
        h.Core.ApplyPolicy(Harness.On() with { MidiDevice = "Some Other Synth" });

        Assert.Equal(MusicStatus.Playing, h.State.Status);
        Assert.Equal("Some Other Synth", h.Players.Last.Context.MidiDevice);
    }

    [Fact]
    public void A_busy_new_midi_output_waits_like_any_start()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer first = h.Players.Last;
        h.Players.OpenError = _ => new MidiOutputException(4, "busy");

        h.Core.ApplyPolicy(Harness.On() with { MidiDevice = "Some Other Synth" });

        Assert.True(first.IsDisposed);
        Assert.Equal(MusicStatus.WaitingForSynthesizer, h.State.Status);
        h.Players.OpenError = null;
        h.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(MusicStatus.Playing, h.State.Status);
        Assert.Equal("Some Other Synth", h.Players.Last.Context.MidiDevice);
    }

    [Fact]
    public void Turning_music_off_cancels_a_play_now_waiting_for_the_synthesizer()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        h.Players.OpenError = _ => new MidiOutputException(4, "busy");
        h.Core.PlayNow(h.Songs[1].Id);
        Assert.Equal(MusicStatus.WaitingForSynthesizer, h.State.Status);

        h.Core.ApplyPolicy(new MusicPolicy { Enabled = false });
        h.Players.OpenError = null;
        h.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(1, h.Players.Count);
        Assert.Equal(MusicStatus.Off, h.State.Status);
    }

    [Fact]
    public void Without_speakers_music_waits_for_an_output_device()
    {
        var h = new Harness();
        h.Environment.HasDevice = false;
        h.Core.ApplyPolicy(Harness.On());

        Assert.Equal(MusicStatus.NoOutputDevice, h.State.Status);
        h.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0, h.Players.Count);
        h.Environment.HasDevice = true;
        h.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, h.Players.Count);
    }

    [Fact]
    public void A_rescan_starts_a_new_bag()
    {
        var songs = TestSongs.Numbered(3);
        var shuffle = new ScriptedRandom(0, 0, 0);
        var h = new Harness(songs, shuffle);
        h.Core.ApplyPolicy(Harness.On());
        h.Players.Last.Finish();
        h.Advance(TimeSpan.FromSeconds(1));
        h.Players.Last.Finish();

        h.Core.OnLibraryChanged([.. songs, TestSongs.Bundled("Song 99")]);
        h.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([3, 2, 4], shuffle.Requests);
        Assert.Equal(songs[0].Id, h.Players.Last.Song.Id);
    }

    [Fact]
    public void Removing_the_playing_song_starts_another()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;

        h.Core.OnLibraryChanged(h.Songs.Where(s => s.Id != playing.Song.Id).ToList());

        Assert.True(playing.IsStopped);
        Assert.Equal(2, h.Players.Count);
    }

    [Fact]
    public void Exit_fades_out_then_stops_and_nothing_plays_again()
    {
        var h = new Harness();
        h.Players.Configure = p => p.OutputLatency = TimeSpan.FromMilliseconds(150);
        h.Core.ApplyPolicy(Harness.On());
        FakeSongPlayer playing = h.Players.Last;

        Task stopped = h.Core.BeginShutdown(TimeSpan.FromMilliseconds(500));

        Assert.Equal(TimeSpan.FromMilliseconds(500), playing.FadeOutDuration);
        Assert.False(stopped.IsCompleted);
        Assert.Equal(h.Core.Now + TimeSpan.FromMilliseconds(650), h.Core.NextWakeUp);
        h.Advance(TimeSpan.FromMilliseconds(650));
        Assert.True(stopped.IsCompletedSuccessfully);
        Assert.True(playing.IsStopped);
        h.Core.ApplyPolicy(Harness.On(PlayMode.Intermittently));
        h.Core.NextSong();
        Assert.Equal(1, h.Players.Count);
    }

    [Fact]
    public void Ticks_keep_the_timeline_aligned_and_watch_the_mixer()
    {
        var h = new Harness();
        h.Environment.Muted = true;
        h.Core.ApplyPolicy(Harness.On());

        for (int i = 0; i < 6; i++)
        {
            h.Core.Tick();
        }

        Assert.Equal(6, h.Players.Last.Calls.Count(c => c == "Synchronize"));
        Assert.Equal(2, h.Environment.MixerChecks);
        Assert.True(h.State.MutedInMixer);
    }

    [Fact]
    public void Song_state_events_follow_the_music()
    {
        var h = new Harness();
        h.Core.ApplyPolicy(Harness.On());
        h.Core.Pause();
        h.Core.Resume();
        h.Players.Last.Finish();

        Assert.Equal(
            [MusicEventKind.SongStarted, MusicEventKind.Paused, MusicEventKind.Resumed, MusicEventKind.SongStopped],
            h.EventKinds());
    }

    [Fact]
    public void Seek_reaches_seekable_songs_only()
    {
        var h = new Harness();
        h.Players.Configure = p => p.CanSeek = true;
        h.Core.ApplyPolicy(Harness.On());

        h.Core.Seek(TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), h.Players.Last.SeekedTo);
        Assert.True(h.State.CanSeek);
    }

    private static HashSet<string> Disabled(params string[] ids) => new(ids, MediaIds.Comparer);

    private static int[] GoldenSequence(int songCount, uint seed)
    {
        using JsonDocument golden = GoldenData.ReadJson("shuffle.json");
        return golden.RootElement.GetProperty("sequences").EnumerateArray()
            .First(s => s.GetProperty("songCount").GetInt32() == songCount && s.GetProperty("seed").GetUInt32() == seed)
            .GetProperty("indices").EnumerateArray().Select(i => i.GetInt32()).ToArray();
    }

    /// <summary>A core on a manual clock with fake engines; engine callbacks run synchronously.</summary>
    private sealed class Harness
    {
        public Harness(int songs = 5, IMusicRandom? shuffle = null, int[]? gapDraws = null)
            : this(TestSongs.Numbered(songs), shuffle, gapDraws)
        {
        }

        public Harness(IReadOnlyList<SongInfo> songs, IMusicRandom? shuffle = null, int[]? gapDraws = null)
        {
            Songs = songs;
            GapRandom = new ScriptedRandom(gapDraws ?? []);
            var hub = new MusicEventHub();
            Reader = hub.Subscribe();
            Core = new DirectorCore(Time, Players, shuffle ?? new MsvcRandom(1), GapRandom, Environment, hub, action => action(), NullAppLog.Instance);
            Core.OnLibraryChanged(songs);
        }

        public ManualTimeProvider Time { get; } = new();

        public FakeSongPlayerFactory Players { get; } = new();

        public FakeAudioEnvironment Environment { get; } = new();

        public ScriptedRandom GapRandom { get; }

        public IMusicEventReader Reader { get; }

        public DirectorCore Core { get; }

        public IReadOnlyList<SongInfo> Songs { get; }

        public MusicState State => Core.GetState();

        public static MusicPolicy On(PlayMode mode = PlayMode.Always) => new() { Enabled = true, Mode = mode };

        /// <summary>Moves the clock, waking the core at every due time on the way (as the director's thread does).</summary>
        public void Advance(TimeSpan by)
        {
            TimeSpan target = Core.Now + by;
            for (int wakeUps = 0; Core.NextWakeUp is { } due && due <= target; wakeUps++)
            {
                Assert.True(wakeUps < 10_000, "the core keeps asking to be woken without handling it");
                if (due > Core.Now)
                {
                    Time.Advance(due - Core.Now);
                }

                Core.OnWakeUp();
            }

            Time.Advance(target - Core.Now);
        }

        public List<MusicEventKind> EventKinds()
        {
            var buffer = new MusicEvent[64];
            int count = Reader.Read(buffer);
            return buffer.Take(count).Select(e => e.Kind).ToList();
        }
    }
}
