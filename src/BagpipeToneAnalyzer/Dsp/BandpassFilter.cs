namespace BagpipeToneAnalyzer.Dsp;

/// <summary>
/// Isolates the Great Highland Bagpipe chanter's fundamental frequency range from the rest of
/// the mix (drones, breath noise) using cascaded high-pass and low-pass biquads. Two stages
/// of each are cascaded for a steeper (24 dB/octave) rolloff, since the chanter's lowest note
/// sits less than an octave above the drones.
/// </summary>
public static class BandpassFilter
{
    public const double LowCutoffHz = 300.0;
    public const double HighCutoffHz = 1200.0;

    public static float[] Apply(float[] samples, int sampleRate)
    {
        var stages = new[]
        {
            BiquadFilter.HighPass(sampleRate, LowCutoffHz),
            BiquadFilter.HighPass(sampleRate, LowCutoffHz),
            BiquadFilter.LowPass(sampleRate, HighCutoffHz),
            BiquadFilter.LowPass(sampleRate, HighCutoffHz),
        };

        var output = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = samples[i];
            foreach (var stage in stages)
            {
                sample = stage.Process(sample);
            }
            output[i] = sample;
        }

        return output;
    }
}
