using BagpipeToneAnalyzer.Analysis;
using BagpipeToneAnalyzer.Audio;
using BagpipeToneAnalyzer.Dsp;
using BagpipeToneAnalyzer.Reporting;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: BagpipeToneAnalyzer <path-to-recording.mp3>");
    return 1;
}

string path = args[0];

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

if (!string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Only MP3 files are currently supported.");
    return 1;
}

try
{
    Console.WriteLine($"Loading {path}...");
    AudioBuffer audio = Mp3Loader.Load(path);
    Console.WriteLine($"Decoded {audio.DurationSeconds:F1}s of audio at {audio.SampleRate} Hz.");

    Console.WriteLine("Isolating chanter frequency range...");
    float[] filtered = BandpassFilter.Apply(audio.Samples, audio.SampleRate);

    Console.WriteLine("Tracking pitch...");
    var pitchDetector = new YinPitchDetector();
    List<PitchFrame> frames = pitchDetector.Analyze(filtered, audio.SampleRate);

    var segmenter = new NoteSegmenter();
    List<NoteSegment> segments = segmenter.Segment(frames);

    var analyzer = new SteadinessAnalyzer();
    AnalysisResult result = analyzer.Analyze(segments);

    ConsoleReporter.Report(path, result);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Analysis failed: {ex.Message}");
    return 1;
}
