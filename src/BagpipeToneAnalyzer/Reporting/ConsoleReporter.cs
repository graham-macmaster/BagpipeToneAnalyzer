using BagpipeToneAnalyzer.Analysis;

namespace BagpipeToneAnalyzer.Reporting;

public static class ConsoleReporter
{
    public static void Report(string filePath, AnalysisResult result)
    {
        Console.WriteLine();
        Console.WriteLine($"Bagpipe Tone Analyzer - {Path.GetFileName(filePath)}");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();
        Console.WriteLine($"Overall steadiness rating: {result.OverallScore:F1} / 100");
        Console.WriteLine($"  Within-note steadiness:      {result.WithinNoteScore,5:F1} / 100  (pitch wavering during sustained notes)");
        Console.WriteLine(result.HasAcrossOccurrenceData
            ? $"  Across-occurrence consistency: {result.AcrossOccurrenceScore,4:F1} / 100  (same note landing at the same pitch each time)"
            : "  Across-occurrence consistency: N/A       (no note was sustained more than once)");
        Console.WriteLine();

        if (result.Notes.Count == 0)
        {
            Console.WriteLine("No sustained notes were detected in this recording.");
            return;
        }

        Console.WriteLine($"Detected {result.Notes.Count} sustained note(s):");
        Console.WriteLine();
        Console.WriteLine($"{"#",4}  {"Start",8}  {"Dur (s)",8}  {"Note",6}  {"Freq (Hz)",10}  {"Wobble (cents)",14}  {"Score",6}");
        Console.WriteLine(new string('-', 68));

        int index = 1;
        foreach (var note in result.Notes.OrderBy(n => n.Segment.StartSeconds))
        {
            Console.WriteLine(
                $"{index,4}  {note.Segment.StartSeconds,8:F2}  {note.Segment.DurationSeconds,8:F2}  " +
                $"{note.NoteName,6}  {note.Segment.MedianFrequencyHz,10:F1}  {note.WithinNoteStddevCents,14:F1}  {note.WithinNoteScore,6:F1}");
            index++;
        }
    }
}
