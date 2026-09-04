using WinDots.Core.Design;

namespace WinDots.Core.Tests.Design;

public class ColorMathTests
{
    [Theory]
    [InlineData(0xFF000000u)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0xFF808080u)]
    [InlineData(0xFF008080u)]
    [InlineData(0xFFFF0000u)]
    [InlineData(0xFF00FF00u)]
    [InlineData(0xFF0000FFu)]
    [InlineData(0xFF8FD3C8u)]
    [InlineData(0xFF1F7A6Eu)]
    [InlineData(0xFF123456u)]
    public void OklabRoundTripWithinOne255(uint color)
    {
        var lab = ColorMath.SrgbToOklab(color);
        uint back = ColorMath.OklabToSrgb(lab);

        int er = (int)((color >> 16) & 0xFF) - (int)((back >> 16) & 0xFF);
        int eg = (int)((color >> 8) & 0xFF) - (int)((back >> 8) & 0xFF);
        int eb = (int)(color & 0xFF) - (int)(back & 0xFF);

        Assert.True(Math.Abs(er) <= 1, $"red off by {er}");
        Assert.True(Math.Abs(eg) <= 1, $"green off by {eg}");
        Assert.True(Math.Abs(eb) <= 1, $"blue off by {eb}");
    }

    [Fact]
    public void BlackWhiteContrastIs21()
    {
        double contrast = ColorMath.WcagContrast(0xFF000000u, 0xFFFFFFFFu);
        Assert.Equal(21.0, contrast, 2);
    }

    [Fact]
    public void ContrastIsOrderIndependent()
    {
        Assert.Equal(
            ColorMath.WcagContrast(0xFF101416u, 0xFF8FD3C8u),
            ColorMath.WcagContrast(0xFF8FD3C8u, 0xFF101416u),
            10);
    }

    [Fact]
    public void MidGreyOnWhiteIsAboutFourFourEight()
    {
        double contrast = ColorMath.WcagContrast(0xFF777777u, 0xFFFFFFFFu);
        Assert.Equal(4.48, contrast, 2);
    }

    [Fact]
    public void BlendFullAlphaReturnsTop()
    {
        Assert.Equal(0xFFFF0000u, ColorMath.Blend(0xFFFF0000u, 0xFF00FF00u, 1.0));
    }

    [Fact]
    public void BlendZeroAlphaReturnsBottom()
    {
        Assert.Equal(0xFF00FF00u, ColorMath.Blend(0xFFFF0000u, 0xFF00FF00u, 0.0));
    }

    [Fact]
    public void BlendHalfIsMidpoint()
    {
        uint result = ColorMath.Blend(0xFF000000u, 0xFFFFFFFFu, 0.5);
        Assert.Equal((byte)128, (byte)((result >> 16) & 0xFF));
        Assert.Equal((byte)128, (byte)((result >> 8) & 0xFF));
        Assert.Equal((byte)128, (byte)(result & 0xFF));
        Assert.Equal((byte)0xFF, (byte)((result >> 24) & 0xFF));
    }

    [Fact]
    public void ChromaIsZeroForGrey()
    {
        Assert.Equal(0.0, ColorMath.SrgbToOklab(0xFF808080u).Chroma, 6);
    }
}
