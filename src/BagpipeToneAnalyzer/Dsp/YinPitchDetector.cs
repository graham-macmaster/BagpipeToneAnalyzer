using BagpipeToneAnalyzer.Analysis;

namespace BagpipeToneAnalyzer.Dsp;

/// <summary>
/// Frame-based fundamental frequency estimation using the YIN algorithm
/// (de Cheveigné &amp; Kawahara, 2002), restricted to the chanter's plausible pitch range
/// so the lag search stays cheap and octave errors outside the instrument's range are rejected.
/// </summary>
public sealed class YinPitchDetector
{
    private const double MinFrequencyHz = 250.0;
    private const double MaxFrequencyHz = 1400.0;
    private const double AperiodicityThreshold = 0.15;

    public int WindowSize { get; init; } = 1024;
    public int HopSize { get; init; } = 512;

    public List<PitchFrame> Analyze(float[] samples, int sampleRate)
    {
        int minTau = Math.Max(2, (int)(sampleRate / MaxFrequencyHz));
        int maxTau = (int)(sampleRate / MinFrequencyHz);
        int bufferNeeded = WindowSize + maxTau;

        var frameStarts = new List<int>();
        for (int start = 0; start + bufferNeeded <= samples.Length; start += HopSize)
        {
            frameStarts.Add(start);
        }

        var results = new PitchFrame?[frameStarts.Count];

        Parallel.For(0, frameStarts.Count, i =>
        {
            int start = frameStarts[i];
            double? freq = EstimatePitch(samples, start, minTau, maxTau, sampleRate);
            double time = start / (double)sampleRate;
            results[i] = new PitchFrame(time, freq);
        });

        return results.Select(f => f!.Value).ToList();
    }

    private double? EstimatePitch(float[] samples, int start, int minTau, int maxTau, int sampleRate)
    {
        var diff = new double[maxTau + 1];

        // Step 1: difference function d(tau) = sum (x[j] - x[j+tau])^2
        for (int tau = 0; tau <= maxTau; tau++)
        {
            double sum = 0;
            for (int j = 0; j < WindowSize; j++)
            {
                double delta = samples[start + j] - samples[start + j + tau];
                sum += delta * delta;
            }
            diff[tau] = sum;
        }

        // Step 2: cumulative mean normalized difference function
        var cmnd = new double[maxTau + 1];
        cmnd[0] = 1.0;
        double runningSum = 0;
        for (int tau = 1; tau <= maxTau; tau++)
        {
            runningSum += diff[tau];
            cmnd[tau] = diff[tau] * tau / runningSum;
        }

        // Step 3: find the first dip below the threshold within the valid tau range,
        // falling back to the global minimum in that range if nothing dips below it.
        int bestTau = -1;
        for (int tau = minTau; tau <= maxTau; tau++)
        {
            if (cmnd[tau] < AperiodicityThreshold)
            {
                while (tau + 1 <= maxTau && cmnd[tau + 1] < cmnd[tau])
                {
                    tau++;
                }
                bestTau = tau;
                break;
            }
        }

        if (bestTau == -1)
        {
            double minValue = double.MaxValue;
            for (int tau = minTau; tau <= maxTau; tau++)
            {
                if (cmnd[tau] < minValue)
                {
                    minValue = cmnd[tau];
                    bestTau = tau;
                }
            }

            // No convincingly periodic lag in range: treat frame as unvoiced.
            if (minValue >= 0.5)
            {
                return null;
            }
        }

        double refinedTau = ParabolicInterpolate(cmnd, bestTau);
        return sampleRate / refinedTau;
    }

    private static double ParabolicInterpolate(double[] cmnd, int tau)
    {
        if (tau <= 0 || tau >= cmnd.Length - 1)
        {
            return tau;
        }

        double s0 = cmnd[tau - 1];
        double s1 = cmnd[tau];
        double s2 = cmnd[tau + 1];
        double denominator = 2 * (2 * s1 - s2 - s0);

        if (Math.Abs(denominator) < 1e-12)
        {
            return tau;
        }

        double shift = (s2 - s0) / denominator;
        return tau + shift;
    }
}
