namespace BagpipeToneAnalyzer.Audio;

/// <summary>Mono PCM audio, sample values in the range [-1, 1].</summary>
public sealed class AudioBuffer
{
    public float[] Samples { get; }
    public int SampleRate { get; }

    public AudioBuffer(float[] samples, int sampleRate)
    {
        Samples = samples;
        SampleRate = sampleRate;
    }

    public double DurationSeconds => Samples.Length / (double)SampleRate;
}
