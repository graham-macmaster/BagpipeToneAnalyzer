using BagpipeToneAnalyzer.Theory;

namespace BagpipeToneAnalyzer.Analysis;

/// <summary>Steadiness results for a single sustained note.</summary>
public sealed class NoteResult
{
    public required NoteSegment Segment { get; init; }
    public required double WithinNoteStddevCents { get; init; }
    public required double WithinNoteScore { get; init; }

    public string NoteName => PitchMath.NearestNoteName(Segment.MedianFrequencyHz);
}

/// <summary>Full steadiness analysis for a recording.</summary>
public sealed class AnalysisResult
{
    public required List<NoteResult> Notes { get; init; }
    public required double WithinNoteScore { get; init; }
    public required double AcrossOccurrenceScore { get; init; }
    public required bool HasAcrossOccurrenceData { get; init; }
    public required double OverallScore { get; init; }
}
