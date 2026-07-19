using NLayer;

namespace BagpipeToneAnalyzer.Audio;

/// <summary>Decodes MP3 files to mono PCM using the pure-managed NLayer decoder.</summary>
public static class Mp3Loader
{
    public static AudioBuffer Load(string path)
    {
        using var mpegFile = new MpegFile(path)
        {
            StereoMode = StereoMode.DownmixToMono
        };

        int sampleRate = mpegFile.SampleRate;
        var samples = new List<float>();
        var readBuffer = new float[16384];

        int readCount;
        while ((readCount = mpegFile.ReadSamples(readBuffer, 0, readBuffer.Length)) > 0)
        {
            for (int i = 0; i < readCount; i++)
            {
                samples.Add(readBuffer[i]);
            }
        }

        return new AudioBuffer(samples.ToArray(), sampleRate);
    }
}
