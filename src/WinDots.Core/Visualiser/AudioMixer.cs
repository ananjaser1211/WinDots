namespace WinDots.Core.Visualiser;

/// <summary>Pure helpers for shaping raw capture buffers before analysis. BCL only, deterministic.</summary>
public static class AudioMixer
{
    /// <summary>
    /// Down-mixes an interleaved multi-channel frame to mono by averaging the channels of each sample frame.
    /// Returns a new array of length <c>interleaved.Length / channels</c>. A trailing partial frame is ignored.
    /// </summary>
    public static float[] DownmixToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channel count must be at least 1.");
        }

        if (channels == 1)
        {
            return interleaved.ToArray();
        }

        int frames = interleaved.Length / channels;
        float[] mono = new float[frames];
        double inverse = 1.0 / channels;

        for (int f = 0; f < frames; f++)
        {
            int baseIndex = f * channels;
            double sum = 0.0;
            for (int c = 0; c < channels; c++)
            {
                sum += interleaved[baseIndex + c];
            }

            mono[f] = (float)(sum * inverse);
        }

        return mono;
    }
}
