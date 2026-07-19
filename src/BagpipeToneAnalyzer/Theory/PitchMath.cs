namespace BagpipeToneAnalyzer.Theory;

/// <summary>Conversions between Hz, cents, and 12-TET note names (A4 = 440 Hz reference).</summary>
public static class PitchMath
{
    private const double A4Hz = 440.0;
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static double HzToCents(double hz) => 1200.0 * Math.Log2(hz / A4Hz);

    public static double CentsToHz(double cents) => A4Hz * Math.Pow(2.0, cents / 1200.0);

    /// <summary>Nearest 12-TET note name and octave, e.g. "A4", for a frequency in Hz.</summary>
    public static string NearestNoteName(double hz)
    {
        double semitoneFromA4 = 12.0 * Math.Log2(hz / A4Hz);
        int rounded = (int)Math.Round(semitoneFromA4);

        // MIDI note number for A4 is 69.
        int midi = 69 + rounded;
        int noteIndex = ((midi % 12) + 12) % 12;
        int octave = (midi / 12) - 1;

        return $"{NoteNames[noteIndex]}{octave}";
    }
}
