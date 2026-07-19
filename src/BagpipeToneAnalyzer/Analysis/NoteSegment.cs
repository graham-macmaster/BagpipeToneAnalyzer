namespace BagpipeToneAnalyzer.Analysis;

/// <summary>A run of consecutive, pitch-continuous voiced frames identified as one sustained note.</summary>
public sealed class NoteSegment
{
    public required List<PitchFrame> Frames { get; init; }

    public double StartSeconds => Frames[0].TimeSeconds;
    public double EndSeconds => Frames[^1].TimeSeconds;
    public double DurationSeconds => EndSeconds - StartSeconds;

    /// <summary>Median frequency across the segment's frames, in Hz.</summary>
    public double MedianFrequencyHz
    {
        get
        {
            var sorted = Frames.Select(f => f.FrequencyHz!.Value).OrderBy(f => f).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }
    }
}
