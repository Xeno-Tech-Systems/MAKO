# Audio — sound, synth, music

```mako
using Audio;
```

No init call needed — the audio device opens on first use. Works with or
without a window.

## Sound effects

```mako
beep = Audio.load("assets/beep.wav");    # wav/ogg/mp3/flac
Audio.play(beep);
```

| Function | Description |
|---|---|
| `load(path)` | Load a file → handle |
| `play(h)` | Play. **Polyphonic** — 8 voices per sound; overlapping plays mix instead of cutting each other off |
| `stop(h)` / `pause(h)` / `resume(h)` | Control all voices of a sound |
| `playing(h)` | Any voice still playing? |
| `volume(h, 0..1)` / `pitch(h, mult)` / `pan(h, 0..1)` | Per-sound settings (0 = left, 0.5 = centre, 1 = right) |

## Synthesizer — sounds from code

No files needed; sounds are generated at runtime:

```mako
laser = Audio.tone("saw", 880, 0.2);         # waveform, Hz, seconds
note  = Audio.note("sine", "F#4", 0.3);      # musical note names, A4 = 440
tune  = Audio.melody("square",
          ["C4", "E4", "G4", "C5:2", "R:0.5", "G4"],  # :n = beats, R = rest
          120);                               # BPM
Audio.play(tune);
```

Waveforms: `"sine"` (soft), `"square"` (chiptune), `"saw"` (buzzy),
`"triangle"` (mellow), `"noise"` (percussion / effects).

Note names: letter + optional `#`/`b` + octave — `"C4"`, `"F#3"`, `"Bb5"`.
All synth sounds get a click-free attack/release envelope.

## Positional sound

```mako
Audio.play_at(snd, x, y);                      # 2D: pans + fades relative to
                                               # the window centre
Audio.play_3d(snd, sx, sy, sz,  lx, ly, lz);   # 3D: source pos, listener pos
```

`play_3d` volume falls off with distance and pans by the listener-relative X
offset. In Mako3D, use the camera as the listener:

```mako
p = Mako3D.camera_pos(cam);
Audio.play_3d(snd, gem_x, 1, gem_z,  p[0], p[1], p[2]);
```

## Music (streamed — for long tracks)

```mako
song = Audio.music_load("track.ogg");
Audio.music_play(song);
# every frame:
Audio.music_update(song);      # REQUIRED — feeds the stream
```

| Function | Description |
|---|---|
| `music_load(path)` → handle | `music_play` `music_stop` `music_pause` `music_resume` |
| `music_update(h)` | Call every frame while playing |
| `music_playing(h)` | Still going? |
| `music_volume(h, 0..1)` | Volume |
| `music_seek(h, seconds)` | Jump |
| `music_length(h)` / `music_pos(h)` | Duration / progress in seconds |

## Global

| Function | Description |
|---|---|
| `master_volume(0..1)` | Set overall volume |
| `master()` | Read it back |
