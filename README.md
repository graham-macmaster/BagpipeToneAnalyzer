# Bagpipe Tone Analyzer

TL;DR: point this tool at an MP3 recording of a solo Great Highland Bagpipe and it prints a steadiness rating out of 100, based on how much the piper's pitch wavers.

Steady blowing is one of the hardest things to nail on the pipes. A four-beat B should sound just as rock-solid at beat four as it did at beat one. A D in an MSR's 2/4 march should sound like the same D when it shows up in the strathspey or the reel. This tool tries to put a number on that.

## What you get

- An overall steadiness rating out of 100
- A within-note score: how much pitch wavers during individual sustained notes
- An across-occurrence score: how consistently the same note is pitched everywhere it occurs in the recording
- A note-by-note table showing every sustained note it detected, its pitch, duration, and how steady it was

```
Overall steadiness rating: 75.2 / 100
  Within-note steadiness:       81.1 / 100  (pitch wavering during sustained notes)
  Across-occurrence consistency: 69.3 / 100  (same note landing at the same pitch each time)

Detected 1046 sustained note(s):

   #     Start   Dur (s)    Note   Freq (Hz)  Wobble (cents)   Score
--------------------------------------------------------------------
   1     10.13      0.20     A#4       454.4            18.1    53.3
   2     10.39      0.12     A#4       453.0            14.4    60.7
   3     16.32      0.61      B4       481.1             7.0    78.6
   ...
```

A score of 100 means dead-steady pitch with no measurable wavering. Lower scores mean more audible pitch movement, either wobble within a note, or the same note landing at noticeably different pitches at different points in the recording. A perfect 100 score is impossible for a human to produce prove me wrong!

Note the note names (A#4, B4, etc.) are just the nearest standard pitch. Most bagpipe chanters are tuned sharper than concert pitch, so don't expect these to line up with what a piper would call the note. What shows up here as "A#4" is likely what a piper calls "Low A."

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download).
- An MP3 recording to analyze. That's currently the only supported input format. Some sample files are provided in the `samples` directory.

No ffmpeg or other external tools needed. MP3 decoding is handled entirely in .NET.

## Usage

```sh
dotnet run --project src/BagpipeToneAnalyzer -c Release -- path/to/recording.mp3
```

The `-c Release` flag matters. It's roughly 5-10x faster than a debug build on longer recordings. A typical multi-minute recording analyzes in a few seconds.

Or build once and run the resulting binary directly:

```sh
dotnet build -c Release
./src/BagpipeToneAnalyzer/bin/Release/net10.0/BagpipeToneAnalyzer path/to/recording.mp3
```

## How it works

1. The recording is decoded and mixed down to a single mono track.
2. The audio is filtered to isolate the chanter's melody and filter out the constant drone notes running underneath it. Without this, the drones would confuse pitch detection.
3. The tool tracks pitch continuously throughout the recording, in short overlapping slices of time.
4. Consecutive slices with a stable, continuous pitch get grouped into individual "notes." Very short blips (grace notes, doublings, toarluaths, etc.) are ignored. This tool is interested in "the black notes," not their embellishments.
5. For each note, it measures how much the pitch drifted while the note was held, and converts that into a 0-100 steadiness score.
6. It also groups every instance of the "same" note across the whole recording and checks how consistent their pitches were with each other.

## Limitations

- MP3 input only, for now.
- Works best on a single unaccompanied piper. It isn't designed for ensemble or band recordings.
- It measures pitch steadiness specifically. It doesn't evaluate volume, dynamics, tone quality, rhythm, or embellishment technique.
- Very short or very quiet recordings may not produce enough sustained notes for a meaningful rating.

## Project layout

See [`AGENTS.md`](AGENTS.md) for a full technical breakdown of the analysis pipeline.
