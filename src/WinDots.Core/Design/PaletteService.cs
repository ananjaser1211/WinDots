using WinDots.Core.Contracts;

namespace WinDots.Core.Design;

/// <summary>
/// Derives an accessible <see cref="Palette"/> from artwork by k-means clustering in Oklab,
/// following _docs/04-visual-design.md "Artwork palette extraction". Pure and deterministic:
/// the same pixels always produce the same palette.
/// </summary>
public sealed class PaletteService : IPaletteService
{
    private const int MaxDimension = 64;
    private const int MinOpaquePixels = 16;
    private const int Clusters = 5;
    private const int Iterations = 8;
    private const int MaxLightnessSteps = 30;
    private const double LightnessStep = 0.02;
    private const double MinContrast = 4.5;

    private const uint SurfaceDark = 0xFF101416;
    private const uint SurfaceLight = 0xFFF4F6F7;
    private const uint FallbackDark = 0xFF8FD3C8;
    private const uint FallbackLight = 0xFF1F7A6E;
    private const uint Black = 0xFF101416;
    private const uint White = 0xFFFFFFFF;

    private const double ContainerAlpha = 0.18;
    private const double BlobAlpha = 0x14 / 255.0;

    /// <inheritdoc />
    /// <remarks>
    /// Used by <c>appearance.paletteSource = fixed</c>: the caller-chosen accent is lightness-adjusted for the
    /// same AA contrast floor as an extracted accent, then the container/blob/on-accent are derived identically.
    /// </remarks>
    public Palette FromAccent(uint accent, bool darkTheme)
    {
        uint surface = darkTheme ? SurfaceDark : SurfaceLight;
        uint adjusted = AdjustForContrast(ColorMath.SrgbToOklab(accent), surface, darkTheme);
        return BuildPalette(adjusted, darkTheme, isFallback: false);
    }

    /// <inheritdoc />
    public Palette Fallback(bool darkTheme) =>
        BuildPalette(darkTheme ? FallbackDark : FallbackLight, darkTheme, isFallback: true);

    /// <inheritdoc />
    public Palette FromArtwork(ReadOnlySpan<byte> bgra, int width, int height, bool darkTheme)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return Fallback(darkTheme);
        }

        var samples = Downsample(bgra, width, height);
        if (samples.Count < MinOpaquePixels)
        {
            return Fallback(darkTheme);
        }

        var accent = ExtractAccent(samples, darkTheme);
        return accent is null
            ? Fallback(darkTheme)
            : BuildPalette(accent.Value, darkTheme, isFallback: false);
    }

    /// <summary>Nearest-neighbour downsample to at most 64x64, keeping only opaque (alpha &gt;= 128) pixels as Oklab.</summary>
    private static List<ColorMath.Oklab> Downsample(ReadOnlySpan<byte> bgra, int width, int height)
    {
        int tw = Math.Min(width, MaxDimension);
        int th = Math.Min(height, MaxDimension);
        var samples = new List<ColorMath.Oklab>(tw * th);

        for (int ty = 0; ty < th; ty++)
        {
            int sy = (int)((long)ty * height / th);
            for (int tx = 0; tx < tw; tx++)
            {
                int sx = (int)((long)tx * width / tw);
                int i = ((sy * width) + sx) * 4;
                byte b = bgra[i];
                byte g = bgra[i + 1];
                byte r = bgra[i + 2];
                byte a = bgra[i + 3];
                if (a < 128)
                {
                    continue;
                }

                samples.Add(ColorMath.SrgbToOklab(ColorMath.Pack(r, g, b)));
            }
        }

        return samples;
    }

    /// <summary>Runs k-means and returns the contrast-adjusted accent, or null when no cluster is usable.</summary>
    private static uint? ExtractAccent(List<ColorMath.Oklab> samples, bool darkTheme)
    {
        var centroids = new ColorMath.Oklab[Clusters];
        for (int k = 0; k < Clusters; k++)
        {
            // Deterministic seed: initial centroids by fixed index spacing across the sample list.
            int index = (int)((long)k * samples.Count / Clusters);
            centroids[k] = samples[index];
        }

        var assignment = new int[samples.Count];
        var populations = new int[Clusters];

        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int s = 0; s < samples.Count; s++)
            {
                assignment[s] = NearestCentroid(samples[s], centroids);
            }

            var sumL = new double[Clusters];
            var sumA = new double[Clusters];
            var sumB = new double[Clusters];
            Array.Clear(populations);

            for (int s = 0; s < samples.Count; s++)
            {
                int c = assignment[s];
                sumL[c] += samples[s].L;
                sumA[c] += samples[s].A;
                sumB[c] += samples[s].B;
                populations[c]++;
            }

            for (int k = 0; k < Clusters; k++)
            {
                if (populations[k] > 0)
                {
                    centroids[k] = new ColorMath.Oklab(
                        sumL[k] / populations[k],
                        sumA[k] / populations[k],
                        sumB[k] / populations[k]);
                }
            }
        }

        // The spec (_docs/04-visual-design.md step 3) defines a lightness band only for
        // dark mode: discard clusters outside [0.25, 0.85]. In light mode there is no such
        // band - step 4 darkens a too-light accent against the light Surface instead of
        // discarding it, so every populated cluster stays a candidate here.
        double lowL = darkTheme ? 0.25 : 0.0;
        double highL = darkTheme ? 0.85 : 1.0;

        int best = -1;
        double bestScore = double.NegativeInfinity;
        for (int k = 0; k < Clusters; k++)
        {
            if (populations[k] == 0 || centroids[k].L < lowL || centroids[k].L > highL)
            {
                continue;
            }

            double score = centroids[k].Chroma * Math.Sqrt(populations[k]);
            if (score > bestScore)
            {
                bestScore = score;
                best = k;
            }
        }

        if (best < 0)
        {
            return null;
        }

        uint surface = darkTheme ? SurfaceDark : SurfaceLight;
        return AdjustForContrast(centroids[best], surface, darkTheme);
    }

    private static int NearestCentroid(ColorMath.Oklab sample, ColorMath.Oklab[] centroids)
    {
        int best = 0;
        double bestDist = double.PositiveInfinity;
        for (int k = 0; k < centroids.Length; k++)
        {
            double dl = sample.L - centroids[k].L;
            double da = sample.A - centroids[k].A;
            double db = sample.B - centroids[k].B;
            double dist = (dl * dl) + (da * da) + (db * db);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = k;
            }
        }

        return best;
    }

    /// <summary>Nudges lightness toward the readable side until contrast vs Surface reaches AA (4.5:1).</summary>
    private static uint AdjustForContrast(ColorMath.Oklab accent, uint surface, bool darkTheme)
    {
        // Dark surface -> lighten the accent; light surface -> darken it.
        double direction = darkTheme ? 1.0 : -1.0;
        var lab = accent;

        for (int step = 0; step <= MaxLightnessSteps; step++)
        {
            uint color = ColorMath.OklabToSrgb(lab);
            if (ColorMath.WcagContrast(color, surface) >= MinContrast)
            {
                return color;
            }

            double nextL = Math.Clamp(lab.L + (direction * LightnessStep), 0.0, 1.0);
            lab = lab with { L = nextL };
        }

        return ColorMath.OklabToSrgb(lab);
    }

    private static Palette BuildPalette(uint accent, bool darkTheme, bool isFallback)
    {
        uint surface = darkTheme ? SurfaceDark : SurfaceLight;
        uint onAccent = ColorMath.WcagContrast(White, accent) >= ColorMath.WcagContrast(Black, accent)
            ? White
            : Black;
        uint container = ColorMath.Blend(accent, surface, ContainerAlpha);
        uint blob = ColorMath.Blend(accent, surface, BlobAlpha);
        return new Palette(accent, onAccent, container, blob, isFallback);
    }
}
