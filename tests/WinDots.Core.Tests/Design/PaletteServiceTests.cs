using WinDots.Core.Design;

namespace WinDots.Core.Tests.Design;

public class PaletteServiceTests
{
    private const uint SurfaceDark = 0xFF101416;
    private const uint SurfaceLight = 0xFFF4F6F7;

    private readonly PaletteService _service = new();

    private static byte[] SolidImage(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var buffer = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            buffer[(i * 4) + 0] = b;
            buffer[(i * 4) + 1] = g;
            buffer[(i * 4) + 2] = r;
            buffer[(i * 4) + 3] = a;
        }

        return buffer;
    }

    private static void SetPixel(byte[] buffer, int width, int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = ((y * width) + x) * 4;
        buffer[i + 0] = b;
        buffer[i + 1] = g;
        buffer[i + 2] = r;
        buffer[i + 3] = a;
    }

    [Fact]
    public void SolidTealExtractsAccentWithContrast()
    {
        var image = SolidImage(64, 64, 0, 128, 128);

        var palette = _service.FromArtwork(image, 64, 64, darkTheme: true);

        Assert.False(palette.IsFallback);
        Assert.True(ColorMath.WcagContrast(palette.Accent, SurfaceDark) >= 4.5);
    }

    [Fact]
    public void NearBlackHasLightnessAdjustedUpToMeetContrast()
    {
        // A dark grey whose lightness is inside the accepted band but whose raw
        // contrast against the dark Surface is far below 4.5:1.
        var image = SolidImage(64, 64, 0x30, 0x30, 0x30);

        double rawContrast = ColorMath.WcagContrast(0xFF303030u, SurfaceDark);
        var palette = _service.FromArtwork(image, 64, 64, darkTheme: true);

        Assert.False(palette.IsFallback);
        Assert.True(rawContrast < 4.5, "precondition: raw colour fails AA");
        Assert.True(ColorMath.WcagContrast(palette.Accent, SurfaceDark) >= 4.5);
        // Adjustment lightens the accent for the dark theme.
        Assert.True(ColorMath.RelativeLuminance(palette.Accent) > ColorMath.RelativeLuminance(0xFF303030u));
    }

    [Fact]
    public void MoreChromaticOrLargerRegionWins()
    {
        // Three quarters blue, one quarter red: blue has the larger population.
        var image = SolidImage(64, 64, 0, 0, 0);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                if (x < 16)
                {
                    SetPixel(image, 64, x, y, 255, 0, 0);
                }
                else
                {
                    SetPixel(image, 64, x, y, 0, 0, 255);
                }
            }
        }

        var palette = _service.FromArtwork(image, 64, 64, darkTheme: true);

        Assert.False(palette.IsFallback);
        Assert.True(ColorMath.WcagContrast(palette.Accent, SurfaceDark) >= 4.5);
        byte r = (byte)((palette.Accent >> 16) & 0xFF);
        byte b = (byte)(palette.Accent & 0xFF);
        Assert.True(b > r, "the dominant blue region should drive the accent");
    }

    [Fact]
    public void TransparentImageFallsBack()
    {
        var image = SolidImage(64, 64, 0, 128, 128, a: 0);

        var palette = _service.FromArtwork(image, 64, 64, darkTheme: true);

        Assert.True(palette.IsFallback);
        Assert.Equal(_service.Fallback(darkTheme: true), palette);
    }

    [Fact]
    public void TinyImageFallsBack()
    {
        var image = SolidImage(2, 2, 0, 128, 128);

        var palette = _service.FromArtwork(image, 2, 2, darkTheme: true);

        Assert.True(palette.IsFallback);
    }

    [Fact]
    public void FallbackUsesThemeSpecificAccent()
    {
        Assert.Equal(0xFF8FD3C8u, _service.Fallback(darkTheme: true).Accent);
        Assert.Equal(0xFF1F7A6Eu, _service.Fallback(darkTheme: false).Accent);
        Assert.True(_service.Fallback(darkTheme: true).IsFallback);
    }

    [Fact]
    public void ExtractionIsDeterministic()
    {
        var image = SolidImage(48, 48, 0, 0, 0);
        var rng = new Random(1234);
        for (int i = 0; i < 48 * 48; i++)
        {
            image[(i * 4) + 0] = (byte)rng.Next(256);
            image[(i * 4) + 1] = (byte)rng.Next(256);
            image[(i * 4) + 2] = (byte)rng.Next(256);
            image[(i * 4) + 3] = 255;
        }

        var a = _service.FromArtwork(image, 48, 48, darkTheme: true);
        var b = _service.FromArtwork(image, 48, 48, darkTheme: true);

        Assert.Equal(a, b);
    }

    [Fact]
    public void LightThemeAccentMeetsContrastAgainstLightSurface()
    {
        var image = SolidImage(64, 64, 0, 128, 128);

        var palette = _service.FromArtwork(image, 64, 64, darkTheme: false);

        Assert.False(palette.IsFallback);
        Assert.True(ColorMath.WcagContrast(palette.Accent, SurfaceLight) >= 4.5);
    }

    [Theory]
    [InlineData(255, 255, 0)]   // yellow
    [InlineData(0, 255, 0)]     // green
    [InlineData(0, 255, 255)]   // cyan
    [InlineData(255, 0, 255)]   // magenta
    [InlineData(255, 128, 0)]   // orange
    [InlineData(128, 255, 0)]   // chartreuse
    public void VividArtworkExtractsInLightTheme(byte r, byte g, byte b)
    {
        // Regression: high-lightness vivid colours (Oklab L > 0.70) must be darkened by the
        // adjust step for the light Surface, not discarded as out-of-band into the fallback.
        var image = SolidImage(64, 64, r, g, b);

        var palette = _service.FromArtwork(image, 64, 64, darkTheme: false);

        Assert.False(palette.IsFallback);
        Assert.True(ColorMath.WcagContrast(palette.Accent, SurfaceLight) >= 4.5);
    }

    [Fact]
    public void LightThemeDarkensTooLightAccentInsteadOfFallingBack()
    {
        // A vivid yellow whose raw lightness is far too high to read on the light Surface.
        var image = SolidImage(64, 64, 255, 255, 0);

        var palette = _service.FromArtwork(image, 64, 64, darkTheme: false);

        Assert.False(palette.IsFallback);
        // The adjust step darkened the accent below the raw yellow's luminance.
        Assert.True(ColorMath.RelativeLuminance(palette.Accent) < ColorMath.RelativeLuminance(0xFFFFFF00u));
        Assert.True(ColorMath.WcagContrast(palette.Accent, SurfaceLight) >= 4.5);
    }

    [Fact]
    public void DerivedColoursAreConsistent()
    {
        var image = SolidImage(64, 64, 0, 128, 128);

        var palette = _service.FromArtwork(image, 64, 64, darkTheme: true);

        // OnAccent is one of the two allowed values and is the higher-contrast choice.
        Assert.True(palette.OnAccent is 0xFF101416u or 0xFFFFFFFFu);
        double whiteContrast = ColorMath.WcagContrast(0xFFFFFFFFu, palette.Accent);
        double blackContrast = ColorMath.WcagContrast(0xFF101416u, palette.Accent);
        uint expectedOn = whiteContrast >= blackContrast ? 0xFFFFFFFFu : 0xFF101416u;
        Assert.Equal(expectedOn, palette.OnAccent);
    }
}
