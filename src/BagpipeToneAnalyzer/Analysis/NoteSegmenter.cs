using BagpipeToneAnalyzer.Theory;

namespace BagpipeToneAnalyzer.Analysis;

/// <summary>
/// Groups a stream of per-frame pitch estimates into discrete sustained notes, in stages:
/// 1. <see cref="SplitIntoRawSegments"/> - a new fragment starts whenever the pitch jumps by more
///    than <see cref="PitchJumpThresholdCents"/> from the current fragment's trailing average, or
///    whenever voicing drops out for longer than <see cref="MaxBridgeableGapSeconds"/>.
/// 2. Single-frame/near-single-frame fragments (under <see cref="MinRawSegmentFramesForReconnect"/>
///    frames) are dropped before reconnection: leaving them in would break the link between the
///    two genuine fragments on either side, since each gets compared against whichever stray blip
///    immediately precedes it instead of the real fragment.
/// 3. <see cref="ReconnectAcrossDropouts"/> - stitches fragments back together across a voicing
///    dropout when they land at essentially the same pitch afterward: a chanter choke (a brief
///    cutout, or a squeal that confuses pitch tracking) followed by a resumption reads as two
///    notes unless reconnected, and pitch tracking right around a dropout is exactly where octave
///    errors are most likely, so the check (and the octave realignment it applies) tolerates the
///    fragment landing an octave away as well as the same pitch.
/// 4. <see cref="AbsorbShortOutliers"/> - folds a short, real mid-note pitch excursion (the piper's
///    blowing wanders off and back) into its neighbors when they'd otherwise bridge together fine.
/// 5. What remains is filtered to fragments at least <see cref="MinNoteDurationSeconds"/> long and
///    voiced densely enough (<see cref="IsSufficientlyVoiced"/>) to be a real sustained note, not
///    grace-note ornaments, transients, or a handful of coincidentally similar-pitch blips
///    scattered across bag-up noise before playing starts.
/// </summary>
public sealed class NoteSegmenter
{
    public double PitchJumpThresholdCents { get; init; } = 70.0;
    public int MinRawSegmentFramesForReconnect { get; init; } = 8;
    public double MaxBridgeableGapSeconds { get; init; } = 2.0;
    public double MinNoteDurationSeconds { get; init; } = 0.3;
    public double TrailingAverageWindowSeconds { get; init; } = 0.2;
    public double MinVoicedDensity { get; init; } = 0.5;
    public int MinVoicedFramesForSparseNote { get; init; } = 15;
    public double MinVoicedDensityForSparseNote { get; init; } = 0.08;
    public double MaxOutlierDurationSeconds { get; init; } = 1.0;

    public List<NoteSegment> Segment(List<PitchFrame> frames)
    {
        var rawSegments = SplitIntoRawSegments(frames);
        double frameSpacingSeconds = EstimateFrameSpacingSeconds(frames);

        // Single-frame (or near single-frame) blips are almost always noise rather than a real
        // fragment of a note; leaving them in the reconnection chain breaks the link between the
        // two genuine fragments on either side of them, since each gets compared to whichever
        // stray blip immediately precedes it rather than to the real fragment.
        var significantRawSegments = rawSegments
            .Where(s => s.Frames.Count >= MinRawSegmentFramesForReconnect)
            .ToList();

        var reconnected = AbsorbShortOutliers(ReconnectAcrossDropouts(significantRawSegments));

        return reconnected
            .Where(s => s.DurationSeconds >= MinNoteDurationSeconds)
            .Where(s => IsSufficientlyVoiced(s, frameSpacingSeconds))
            .ToList();
    }

    /// <summary>
    /// A brief, real pitch excursion in the middle of an unsteady note (the piper's blowing
    /// genuinely wanders off and back) can be too far from either neighbor to bridge on its own,
    /// splitting one wobbly note into three. If a short fragment sits between two others that
    /// would themselves bridge together just fine, fold it into them rather than counting it (and
    /// the split it causes) as separate notes. This looks at just the two genuine neighbors, so
    /// unlike scaling the merge tolerance to the note's own wobble, it can't snowball into
    /// swallowing an unrelated later note.
    /// </summary>
    private List<NoteSegment> AbsorbShortOutliers(List<NoteSegment> segments)
    {
        var result = new List<NoteSegment>(segments);

        for (int i = 1; i < result.Count - 1; i++)
        {
            NoteSegment before = result[i - 1];
            NoteSegment outlier = result[i];
            NoteSegment after = result[i + 1];

            if (outlier.DurationSeconds > MaxOutlierDurationSeconds)
            {
                continue;
            }

            double gapSeconds = after.StartSeconds - before.EndSeconds;
            if (gapSeconds > MaxBridgeableGapSeconds)
            {
                continue;
            }

            double beforeTrailingCents = TrailingAverageCents(before.Frames);
            double afterCents = PitchMath.HzToCents(after.MedianFrequencyHz);
            if (OctaveReducedCentsDistance(afterCents, beforeTrailingCents) > PitchJumpThresholdCents)
            {
                continue;
            }

            int outlierShift = (int)Math.Round((beforeTrailingCents - PitchMath.HzToCents(outlier.MedianFrequencyHz)) / 1200.0);
            int afterShift = (int)Math.Round((beforeTrailingCents - afterCents) / 1200.0);

            var combinedFrames = new List<PitchFrame>(before.Frames);
            combinedFrames.AddRange(outlierShift == 0 ? outlier.Frames : Shift(outlier.Frames, outlierShift));
            combinedFrames.AddRange(afterShift == 0 ? after.Frames : Shift(after.Frames, afterShift));

            result[i - 1] = new NoteSegment { Frames = combinedFrames };
            result.RemoveAt(i + 1);
            result.RemoveAt(i);
            i -= 2;
        }

        return result;
    }

    /// <summary>
    /// A real sustained note is voiced almost continuously. A handful of coincidentally
    /// similar-pitch blips scattered across an otherwise unvoiced stretch (e.g. bag-up noise
    /// before playing starts) can pass the density bar's absolute count without being a real
    /// note, so a short segment must be densely voiced, while a longer one gets a lower density
    /// bar provided it still has a substantial number of confirming frames (e.g. a genuinely
    /// held note that pitch tracking mostly lost to interference from a coincident drone
    /// harmonic, rather than sounding through cleanly).
    /// </summary>
    private bool IsSufficientlyVoiced(NoteSegment segment, double frameSpacingSeconds)
    {
        double density = VoicedDensity(segment, frameSpacingSeconds);
        if (density >= MinVoicedDensity)
        {
            return true;
        }

        return segment.Frames.Count >= MinVoicedFramesForSparseNote && density >= MinVoicedDensityForSparseNote;
    }

    private static double EstimateFrameSpacingSeconds(List<PitchFrame> frames)
    {
        for (int i = 1; i < frames.Count; i++)
        {
            double delta = frames[i].TimeSeconds - frames[i - 1].TimeSeconds;
            if (delta > 0)
            {
                return delta;
            }
        }

        return 1.0;
    }

    /// <summary>
    /// Fraction of a segment's timespan that actually has a voiced frame. A genuine sustained
    /// note fills nearly all of its span; a handful of coincidentally similar-pitch blips scattered
    /// across an otherwise unvoiced stretch (e.g. bag-up noise before playing starts) does not,
    /// even though it can pass the duration and pitch-jump checks.
    /// </summary>
    private static double VoicedDensity(NoteSegment segment, double frameSpacingSeconds)
    {
        double expectedFrames = segment.DurationSeconds / frameSpacingSeconds + 1.0;
        return segment.Frames.Count / expectedFrames;
    }

    private List<NoteSegment> SplitIntoRawSegments(List<PitchFrame> frames)
    {
        var segments = new List<NoteSegment>();
        var current = new List<PitchFrame>();
        double? lastVoicedTime = null;

        void FlushCurrent()
        {
            if (current.Count > 0)
            {
                segments.Add(new NoteSegment { Frames = new List<PitchFrame>(current) });
                current.Clear();
            }
        }

        foreach (var frame in frames)
        {
            if (frame.FrequencyHz is not double hz)
            {
                if (lastVoicedTime is double lastTime && frame.TimeSeconds - lastTime > MaxBridgeableGapSeconds)
                {
                    FlushCurrent();
                }
                continue;
            }

            double cents = PitchMath.HzToCents(hz);

            if (current.Count > 0)
            {
                double trailingAverageCents = TrailingAverageCents(current);
                bool tooFarInPitch = Math.Abs(cents - trailingAverageCents) > PitchJumpThresholdCents;
                bool tooLongSinceLastVoiced = lastVoicedTime is double t && frame.TimeSeconds - t > MaxBridgeableGapSeconds;

                if (tooFarInPitch || tooLongSinceLastVoiced)
                {
                    FlushCurrent();
                }
            }

            current.Add(frame);
            lastVoicedTime = frame.TimeSeconds;
        }

        FlushCurrent();
        return segments;
    }

    private List<NoteSegment> ReconnectAcrossDropouts(List<NoteSegment> rawSegments)
    {
        if (rawSegments.Count == 0)
        {
            return rawSegments;
        }

        var merged = new List<List<PitchFrame>> { new(rawSegments[0].Frames) };

        for (int i = 1; i < rawSegments.Count; i++)
        {
            var previousFrames = merged[^1];
            var next = rawSegments[i];

            double gapSeconds = next.StartSeconds - previousFrames[^1].TimeSeconds;
            double previousTrailingCents = TrailingAverageCents(previousFrames);
            double nextCents = PitchMath.HzToCents(next.MedianFrequencyHz);

            bool hadDropout = gapSeconds > 0;
            double pitchDistance = hadDropout
                ? OctaveReducedCentsDistance(nextCents, previousTrailingCents)
                : Math.Abs(nextCents - previousTrailingCents);

            if (gapSeconds <= MaxBridgeableGapSeconds && pitchDistance <= PitchJumpThresholdCents)
            {
                // The fragment may have locked onto an octave-shifted reading of the same note
                // (a common failure mode right around a dropout); realign it to the previous
                // fragment's octave so the merged segment's frequencies are on a consistent scale.
                int octaveShift = (int)Math.Round((previousTrailingCents - nextCents) / 1200.0);
                previousFrames.AddRange(octaveShift == 0 ? next.Frames : Shift(next.Frames, octaveShift));
            }
            else
            {
                merged.Add(new List<PitchFrame>(next.Frames));
            }
        }

        return merged.Select(f => new NoteSegment { Frames = f }).ToList();
    }

    private double TrailingAverageCents(List<PitchFrame> current)
    {
        double windowStart = current[^1].TimeSeconds - TrailingAverageWindowSeconds;
        double sum = 0;
        int count = 0;
        for (int i = current.Count - 1; i >= 0 && current[i].TimeSeconds >= windowStart; i--)
        {
            sum += PitchMath.HzToCents(current[i].FrequencyHz!.Value);
            count++;
        }
        return sum / count;
    }

    private static List<PitchFrame> Shift(List<PitchFrame> frames, int octaveShift)
    {
        double factor = Math.Pow(2.0, octaveShift);
        return frames.ConvertAll(f => new PitchFrame(f.TimeSeconds, f.FrequencyHz * factor));
    }

    private static double OctaveReducedCentsDistance(double a, double b)
    {
        double diff = Math.Abs(a - b) % 1200.0;
        return Math.Min(diff, 1200.0 - diff);
    }
}
