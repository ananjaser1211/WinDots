namespace WinDots.Core.Design;

/// <summary>
/// Pure colour maths shared by the palette pipeline: sRGB &lt;-&gt; linear &lt;-&gt; Oklab
/// conversions, WCAG contrast, and alpha compositing. Colours are packed as
/// <c>0xAARRGGBB</c>. Free of any platform dependency and fully deterministic.
/// </summary>
public static class ColorMath
{
    /// <summary>A point in the Oklab colour space: perceptual lightness and the a/b axes.</summary>
    public readonly record struct Oklab(double L, double A, double B)
    {
        /// <summary>Perceptual chroma, the distance from the neutral axis.</summary>
        public double Chroma => Math.Sqrt((A * A) + (B * B));
    }

    /// <summary>Extracts the alpha channel (0-255) from a packed colour.</summary>
    public static byte Alpha(uint color) => (byte)((color >> 24) & 0xFF);

    /// <summary>Packs opaque RGB bytes into a <c>0xFFRRGGBB</c> colour.</summary>
    public static uint Pack(byte r, byte g, byte b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    /// <summary>Packs a colour with an explicit alpha byte.</summary>
    public static uint Pack(byte a, byte r, byte g, byte b) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    private static (byte R, byte G, byte B) Unpack(uint color) =>
        ((byte)((color >> 16) & 0xFF), (byte)((color >> 8) & 0xFF), (byte)(color & 0xFF));

    private static double SrgbChannelToLinear(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte LinearChannelToSrgb(double linear)
    {
        double c = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055;
        int v = (int)Math.Round(c * 255.0, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(v, 0, 255);
    }

    /// <summary>Converts a packed sRGB colour to linear-light RGB in [0, 1].</summary>
    public static (double R, double G, double B) SrgbToLinear(uint color)
    {
        var (r, g, b) = Unpack(color);
        return (SrgbChannelToLinear(r), SrgbChannelToLinear(g), SrgbChannelToLinear(b));
    }

    /// <summary>Converts linear-light RGB to Oklab.</summary>
    public static Oklab LinearToOklab(double r, double g, double b)
    {
        double l = (0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b);
        double m = (0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b);
        double s = (0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b);

        double lp = Math.Cbrt(l);
        double mp = Math.Cbrt(m);
        double sp = Math.Cbrt(s);

        return new Oklab(
            (0.2104542553 * lp) + (0.7936177850 * mp) - (0.0040720468 * sp),
            (1.9779984951 * lp) - (2.4285922050 * mp) + (0.4505937099 * sp),
            (0.0259040371 * lp) + (0.7827717662 * mp) - (0.8086757660 * sp));
    }

    /// <summary>Converts Oklab back to linear-light RGB (unclamped).</summary>
    public static (double R, double G, double B) OklabToLinear(Oklab lab)
    {
        double lp = lab.L + (0.3963377774 * lab.A) + (0.2158037573 * lab.B);
        double mp = lab.L - (0.1055613458 * lab.A) - (0.0638541728 * lab.B);
        double sp = lab.L - (0.0894841775 * lab.A) - (1.2914855480 * lab.B);

        double l = lp * lp * lp;
        double m = mp * mp * mp;
        double s = sp * sp * sp;

        return (
            (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
            (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
            (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s));
    }

    /// <summary>Converts a packed sRGB colour to Oklab.</summary>
    public static Oklab SrgbToOklab(uint color)
    {
        var (r, g, b) = SrgbToLinear(color);
        return LinearToOklab(r, g, b);
    }

    /// <summary>Converts Oklab to a packed opaque sRGB colour, clamping out-of-gamut channels.</summary>
    public static uint OklabToSrgb(Oklab lab)
    {
        var (r, g, b) = OklabToLinear(lab);
        return Pack(LinearChannelToSrgb(r), LinearChannelToSrgb(g), LinearChannelToSrgb(b));
    }

    /// <summary>WCAG 2.x relative luminance of a packed colour.</summary>
    public static double RelativeLuminance(uint color)
    {
        var (r, g, b) = SrgbToLinear(color);
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    /// <summary>WCAG 2.x contrast ratio between two packed colours (order independent, 1..21).</summary>
    public static double WcagContrast(uint a, uint b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double lighter = Math.Max(la, lb);
        double darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Alpha-composites <paramref name="top"/> over opaque <paramref name="bottom"/> using
    /// <paramref name="alpha"/> in [0, 1] as the coverage of <paramref name="top"/>. The result is opaque.
    /// </summary>
    public static uint Blend(uint top, uint bottom, double alpha)
    {
        double a = Math.Clamp(alpha, 0.0, 1.0);
        var (tr, tg, tb) = Unpack(top);
        var (br, bg, bb) = Unpack(bottom);
        byte r = (byte)Math.Round((tr * a) + (br * (1.0 - a)), MidpointRounding.AwayFromZero);
        byte g = (byte)Math.Round((tg * a) + (bg * (1.0 - a)), MidpointRounding.AwayFromZero);
        byte b = (byte)Math.Round((tb * a) + (bb * (1.0 - a)), MidpointRounding.AwayFromZero);
        return Pack(r, g, b);
    }
}
