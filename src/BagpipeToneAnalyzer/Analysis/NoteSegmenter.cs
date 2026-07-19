using BagpipeToneAnalyzer.Theory;

namespace BagpipeToneAnalyzer.Analysis;

/// <summary>
/// Groups a stream of per-frame pitch estimates into discrete sustained notes: a new note
/// starts whenever the pitch jumps by more than <see cref="PitchJumpThresholdCents"/> from the
/// current note's running average, or whenever voicing drops out for longer than
/// <see cref="MaxBridgeableGapSeconds"/> (a longer silence/noise gap, as opposed to a brief
/// dropout mid-note). Segments shorter than <see cref="MinNoteDurationSeconds"/> are discarded
/// as grace-note ornaments or transients rather than sustained notes worth rating for steadiness.
/// </summary>
public sealed class NoteSegmenter
{
    public double PitchJumpThresholdCents { get; init; } = 45.0;
    public double MaxBridgeableGapSeconds { get; init; } = 0.03;
    public double MinNoteDurationSeconds { get; init; } = 0.08;

    public List<NoteSegment> Segment(List<PitchFrame> frames)
    {
        var segments = new List<NoteSegment>();
        var current = new List<PitchFrame>();
        double? lastVoicedTime = null;

        void FlushCurrent()
        {
            if (current.Count > 0)
            {
                var segment = new NoteSegment { Frames = new List<PitchFrame>(current) };
                if (segment.DurationSeconds >= MinNoteDurationSeconds)
                {
                    segments.Add(segment);
                }
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
                double runningAverageCents = current.Average(f => PitchMath.HzToCents(f.FrequencyHz!.Value));
                bool tooFarInPitch = Math.Abs(cents - runningAverageCents) > PitchJumpThresholdCents;
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
}
