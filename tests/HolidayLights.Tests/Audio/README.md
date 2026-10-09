# tests/HolidayLights.Tests/Audio

Owner: audio. Namespace `HolidayLights.Tests.Audio`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Audio"
```

| File | Covers |
|---|---|
| `MidiFileGoldenTests` | all 47 shipped `.mid` files (46 songs + `reset.mid`) against golden `midi.json`: structure, per-track counts, tempo maps, durations, quarter-note beats |
| `MidiFileParsingTests` | the tolerant SMF reader: running status, RMID, SysEx, damaged tracks, unknown chunks, SMPTE, tempo changes |
| `ShuffleBagGoldenTests` | every sequence and scenario of golden `shuffle.json` with 5.4's MSVC `rand()` (checked against `msvc-rand.json`) |
| `MidiPlaybackTests`, `MidiSequencerTests` | sequencer timing against fake clocks: every message at its exact time, GM System On and its settle time, CC7 volume scaling, fades, pause/resume state re-send, resets, events, device failures; Dance event timing (stamped when heard: send + device latency, 190 ms for the GS synth, + `music.syncOffsetMs`; raised 40 ms ahead; dropped by Pause/Stop) |
| `DirectorCoreTests` | the play-mode table, the 1 s and 60-180 s gaps, the shuffle order in use (golden), the switch, Play Now, Next, Previous, pause rules, Remote Desktop, the held first song, failures, the busy synthesizer, a missing output device, rescans, the exit fade, a "MIDI Output" change moving the song at once |
| `MusicDirectorTests` | the public engine on its music thread with fake engines |
| `MusicEventHubTests` | the lock-free event queues, overflow, concurrent producers |
| `MusicLibraryTests` | bundled songs (5.4 order, credits, chips), My Music, shortcuts, the watcher, Add/Remove/Restore/hide, probing |
| `AudioChainTests` | the beat detector (the audio prototype's verified 18 beats), volume curve and fades, the Sun audio reader, heard-time mapping |
| `AudioHardwareTests` | the real WinMM synthesizer and WaveOut, silently (skipped without devices, or while no audio output exists for the GS synth); two audible opt-in tests |

No test plays audible sound by default. The audible tests (`[Trait("Category", "Live")]`) run only when
`HOLIDAYLIGHTS_LIVE_AUDIO=1` is set: a smoke test (1 s of Jingle Bells at 15 % volume) and the Dance latency probe (three
quiet notes on the GS Wavetable Synth recorded through WASAPI loopback: each note event must be stamped between 15 ms
before and 45 ms after its sound reaches the audio engine, and published at most 60 ms before its stamp).

Helpers: `AudioFakes.cs` (manual time, fake engines, recording MIDI output, jumping and manual sequencer clocks, fake shell), `MsvcRandom.cs`,
`SmfBuilder.cs`; shared ones (owner: contracts) in `../Shared`.
