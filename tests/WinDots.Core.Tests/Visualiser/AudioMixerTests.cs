using WinDots.Core.Visualiser;

namespace WinDots.Core.Tests.Visualiser;

public class AudioMixerTests
{
    [Fact]
    public void MonoPassesThrough()
    {
        float[] input = { 0.1f, -0.2f, 0.3f };
        float[] mono = AudioMixer.DownmixToMono(input, 1);
        Assert.Equal(input, mono);
    }

    [Fact]
    public void StereoAveragesChannels()
    {
        // L/R interleaved: (1,0), (0,1), (0.5,0.5)
        float[] input = { 1f, 0f, 0f, 1f, 0.5f, 0.5f };
        float[] mono = AudioMixer.DownmixToMono(input, 2);

        Assert.Equal(3, mono.Length);
        Assert.Equal(0.5f, mono[0], 6);
        Assert.Equal(0.5f, mono[1], 6);
        Assert.Equal(0.5f, mono[2], 6);
    }

    [Fact]
    public void FiveOneAveragesAllSixChannels()
    {
        float[] input = { 6f, 0f, 0f, 0f, 0f, 0f };
        float[] mono = AudioMixer.DownmixToMono(input, 6);
        Assert.Single(mono);
        Assert.Equal(1f, mono[0], 6);
    }

    [Fact]
    public void TrailingPartialFrameIgnored()
    {
        float[] input = { 1f, 1f, 1f }; // 1.5 stereo frames
        float[] mono = AudioMixer.DownmixToMono(input, 2);
        Assert.Single(mono);
        Assert.Equal(1f, mono[0], 6);
    }

    [Fact]
    public void ZeroChannelsThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioMixer.DownmixToMono(new float[] { 1f }, 0));
    }
}
