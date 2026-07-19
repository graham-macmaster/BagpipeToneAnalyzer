# AGENTS.md

Guidance for AI coding agents working in this repository. See `PROJECT.md` for the product goal.

## What this is

A .NET console app that takes a bagpipe recording (MP3) and prints a "steadiness" rating (0-100): how much a piper's pitch wavers within sustained notes, and how consistently the same note is pitched across different occurrences in a tune.

## Build / run / test

Requires the dotnet SDK pinned in `.tool-versions` (managed via `mise`; run `eval "$(mise activate bash)"` once per shell if `dotnet` isn't on PATH).

```sh
dotnet build
dotnet run --project src/BagpipeToneAnalyzer -- <path-to-recording.mp3>
dotnet run --project src/BagpipeToneAnalyzer -c Release -- <path-to-recording.mp3>  # ~5-10x faster, use for real analysis runs
```

There is no automated test suite yet. The de facto smoke test is running against `test.mp3` in the repo root and checking the output is musically plausible (see "Sanity-checking changes" below).

## Architecture

Pipeline, in order, under `src/BagpipeToneAnalyzer/`:

1. **`Audio/Mp3Loader.cs`** - decodes MP3 to mono PCM float samples using NLayer (pure-managed C#, no ffmpeg/native codec dependency). This is deliberate: the tool needs to run on machines without ffmpeg installed. Uses NLayer's built-in `StereoMode.DownmixToMono`.
2. **`Dsp/BiquadFilter.cs`, `Dsp/BandpassFilter.cs`** - cascaded biquad high-pass + low-pass (300-1200 Hz, 24 dB/octave) to isolate the chanter melody from the drones before pitch tracking. Drones sit roughly an octave or more below the chanter's range. Without this filter, pitch detection gets confused by drone content.
3. **`Dsp/YinPitchDetector.cs`** - YIN algorithm (de Cheveigne and Kawahara) for per-frame fundamental frequency estimation. The lag search is restricted to 320-1400 Hz (the chanter's plausible range, with the floor set above the bandpass filter's 300 Hz cutoff so the search can't lock onto a boundary lag with no real signal behind it), both for speed (avoids a full-range O(W^2) search) and to reject octave errors. The search scans from the smallest lag (highest frequency) upward and stops at the first dip below `AperiodicityThreshold`, which favors the true fundamental over a lower-frequency subharmonic that can otherwise look equally (or more) periodic when strong harmonics are present in the tone; a frame with no dip anywhere in range is unvoiced rather than guessed from a weak match (this is what a genuine choke or breath noise looks like). Frames are processed in parallel via `Parallel.For` since each is independent. This is what keeps a ~5 minute recording under a few seconds to analyze.
4. **`Analysis/NoteSegmenter.cs`** - groups the per-frame pitch stream into discrete sustained notes, in stages: split on a pitch jump or a long voicing gap; drop single-frame noise blips that would otherwise break the link between two real fragments; reconnect fragments across a voicing dropout when they land at essentially the same pitch (or an octave away, since that's exactly where tracking is prone to octave errors) afterward, which is what makes a chanter choke on a held note (a cutout, or a squeal that confuses pitch tracking, followed by a resumption at the same pitch) count as one note rather than two; fold a short, real mid-note pitch excursion into its neighbors when they'd otherwise bridge together fine; then discard whatever's left that's too short or too sparsely voiced to be a real sustained note (grace notes/ornaments, or a handful of coincidentally similar-pitch blips scattered across bag-up noise before playing starts).
5. **`Analysis/SteadinessAnalyzer.cs`** - the actual scoring. Two components, both converted from a cents standard deviation to a 0-100 score via `100 * exp(-stddev/k)` (k is tuned so a 20-cent stddev scores about 50):
   - **Within-note**: pitch stddev inside each note, trimming ~15ms off each edge first to avoid attack/release transients skewing the number.
   - **Across-occurrence**: for notes that recur (grouped by nearest 12-TET name), stddev of their median pitches across occurrences. Notes that never repeat don't contribute. If nothing repeats, the overall score falls back to within-note only (`HasAcrossOccurrenceData = false`).
6. **`Theory/PitchMath.cs`** - Hz/cents/note-name conversions, A4 = 440 Hz reference. Note names are informational labels only (nearest 12-TET pitch class). Bagpipe chanters are typically tuned noticeably sharp of concert pitch, so don't be surprised to see, for example, `A#4` for what a piper would call "Low A".
7. **`Reporting/ConsoleReporter.cs`, `Program.cs`** - CLI argument handling and output formatting.

## Key tunable constants (if steadiness numbers look wrong)

| Constant | Location | Purpose |
| --- | --- | --- |
| `LowCutoffHz` / `HighCutoffHz` (300/1200) | `BandpassFilter` | Chanter isolation band |
| `MinFrequencyHz` / `MaxFrequencyHz` (320/1400) | `YinPitchDetector` | Pitch search range |
| `AperiodicityThreshold` (0.4) | `YinPitchDetector` | YIN voiced/unvoiced cutoff |
| `PitchJumpThresholdCents` (70) | `NoteSegmenter` | New-note boundary sensitivity |
| `MinRawSegmentFramesForReconnect` (8) | `NoteSegmenter` | Drops noise blips before fragment reconnection |
| `MaxBridgeableGapSeconds` (2.0) | `NoteSegmenter` | Longest voicing dropout a choke can bridge |
| `MinNoteDurationSeconds` (0.3) | `NoteSegmenter` | Ornament vs. sustained-note cutoff |
| `TrailingAverageWindowSeconds` (0.2) | `NoteSegmenter` | Recent-pitch window used for jump/reconnect comparisons |
| `MinVoicedDensity` (0.5) | `NoteSegmenter` | Minimum fraction of a segment's span that must be voiced |
| `MinVoicedFramesForSparseNote` / `MinVoicedDensityForSparseNote` (15 / 0.08) | `NoteSegmenter` | Lower density bar for a long-but-sparse note (e.g. tracking mostly lost to drone interference) |
| `MaxOutlierDurationSeconds` (1.0) | `NoteSegmenter` | Longest mid-note excursion that gets folded into its neighbors |
| `EdgeTrimSeconds` (0.015) | `SteadinessAnalyzer` | Attack/release transient trim |
| `ScoreDecayConstant` | `SteadinessAnalyzer` | Cents-stddev to score curve steepness |

## Sanity-checking changes

There's no ground-truth labeled dataset, but `samples/scale_steady.mp3` and `samples/scale_unsteady.mp3` (both the same ascending 9-note GHB scale, one blown steadily and one blown poorly with chokes on the high notes) give a rough check. When touching the DSP/analysis pipeline, re-run against both and eyeball the per-note table:

- Both recordings should detect 9 sustained notes. A choke (the chanter cutting out mid-note and resuming at the same pitch) should count as one note, not two.
- The steady recording's overall steadiness rating should be clearly higher than the unsteady one's.
- Detected note names should trace a recognizable Great Highland Bagpipe scale (roughly 9 notes spanning about a ninth: low, low+step, then a run up to an octave-plus above).
- Frequencies for the same note name should cluster tightly across the piece (allow for the sharp-of-440 tuning mentioned above).
- A `-c Release` run should still finish in single-digit seconds for a ~5 minute recording. If a change makes it noticeably slower, check whether the YIN lag range or window size grew.

## Conventions

- No code comments except where a non-obvious constraint or magic number needs explaining (see the table above for what already has rationale in the code).
- Prefer adding new pipeline stages as their own class in the matching namespace folder (`Audio/`, `Dsp/`, `Analysis/`, `Theory/`, `Reporting/`) over growing existing classes.
- Only MP3 input is supported by design (see `PROJECT.md` scope discussion). Don't add other format support without checking with the user first, since it changes the loader architecture (NLayer is MP3-specific).
- Avoid characters not readily available on an ANSI keyboard in any generated text or program output (hyphens instead of em dashes, straight quotes instead of curly ones, three dots instead of an ellipsis character).

## Keeping this file and README.md current

Both files describe the system as it exists today, not as it was when first written. Whenever a change adds, removes, or restructures a pipeline stage, changes a build/run/test command, adds a tunable constant worth documenting, or changes user-facing behavior:

- Update the relevant section of this file (architecture list, constants table, build/run commands, conventions) in the same change.
- Update `README.md` if the change affects what a user would see there (usage instructions, features, requirements).
- Treat a pull request that changes behavior but leaves these files stale as incomplete, not optional cleanup.
