using BagpipeToneAnalyzer.Theory;

namespace BagpipeToneAnalyzer.Analysis;

/// <summary>
/// Turns note segments into steadiness scores on a 0-100 scale, where 100 is a perfectly
/// steady pitch. Two things are measured, per the brief:
///   1. Within-note steadiness: how much a single sustained note's pitch wavers.
///   2. Across-occurrence consistency: whether the same note (e.g. every D) lands at the same
///      pitch each time it's played, regardless of musical context.
/// Both are converted from a cents standard deviation to a score via a smooth exponential
/// falloff, chosen so that ~0 cents stddev scores 100, ~20 cents scores ~50, and large
/// deviations asymptote toward 0.
/// </summary>
public sealed class SteadinessAnalyzer
{
    // exp(-x/k): k chosen so that a 20-cent stddev (roughly a fifth of a semitone -
    // comfortably audible wavering on a drone-accompanied instrument) scores 50.
    private const double ScoreDecayConstant = 20.0 / 0.6931471805599453; // 20 / ln(2)

    public double EdgeTrimSeconds { get; init; } = 0.015;

    public AnalysisResult Analyze(List<NoteSegment> segments)
    {
        var noteResults = segments.Select(AnalyzeNote).ToList();

        double withinNoteScore = WeightedAverage(
            noteResults.Select(n => (n.WithinNoteScore, n.Segment.DurationSeconds)));

        var (acrossScore, hasAcrossData) = AnalyzeAcrossOccurrences(noteResults);

        double overallScore = hasAcrossData
            ? (withinNoteScore + acrossScore) / 2.0
            : withinNoteScore;

        return new AnalysisResult
        {
            Notes = noteResults,
            WithinNoteScore = withinNoteScore,
            AcrossOccurrenceScore = acrossScore,
            HasAcrossOccurrenceData = hasAcrossData,
            OverallScore = overallScore,
        };
    }

    private NoteResult AnalyzeNote(NoteSegment segment)
    {
        double startCore = segment.StartSeconds + EdgeTrimSeconds;
        double endCore = segment.EndSeconds - EdgeTrimSeconds;

        var coreFrames = segment.Frames
            .Where(f => f.TimeSeconds >= startCore && f.TimeSeconds <= endCore)
            .ToList();

        if (coreFrames.Count < 2)
        {
            coreFrames = segment.Frames;
        }

        double stddevCents = CentsStandardDeviation(coreFrames.Select(f => f.FrequencyHz!.Value));

        return new NoteResult
        {
            Segment = segment,
            WithinNoteStddevCents = stddevCents,
            WithinNoteScore = ScoreFromStddevCents(stddevCents),
        };
    }

    private static (double score, bool hasData) AnalyzeAcrossOccurrences(List<NoteResult> notes)
    {
        var groups = notes
            .GroupBy(n => n.NoteName)
            .Where(g => g.Count() >= 2)
            .ToList();

        if (groups.Count == 0)
        {
            return (0, false);
        }

        var weightedScores = groups.Select(group =>
        {
            var medianCents = group.Select(n => PitchMath.HzToCents(n.Segment.MedianFrequencyHz));
            double stddev = StandardDeviation(medianCents);
            double totalDuration = group.Sum(n => n.Segment.DurationSeconds);
            return (score: ScoreFromStddevCents(stddev), weight: totalDuration);
        });

        return (WeightedAverage(weightedScores), true);
    }

    private static double CentsStandardDeviation(IEnumerable<double> frequenciesHz)
        => StandardDeviation(frequenciesHz.Select(PitchMath.HzToCents));

    private static double StandardDeviation(IEnumerable<double> values)
    {
        var list = values.ToList();
        double mean = list.Average();
        double variance = list.Sum(v => (v - mean) * (v - mean)) / list.Count;
        return Math.Sqrt(variance);
    }

    private static double ScoreFromStddevCents(double stddevCents)
        => Math.Clamp(100.0 * Math.Exp(-stddevCents / ScoreDecayConstant), 0.0, 100.0);

    private static double WeightedAverage(IEnumerable<(double value, double weight)> items)
    {
        var list = items.ToList();
        double totalWeight = list.Sum(i => i.weight);
        if (totalWeight <= 0)
        {
            return list.Count > 0 ? list.Average(i => i.value) : 0;
        }
        return list.Sum(i => i.value * i.weight) / totalWeight;
    }
}
