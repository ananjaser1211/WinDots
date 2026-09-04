namespace WinDots.Core.Visualiser;

/// <summary>
/// A self-contained, allocation-light radix-2 iterative Cooley-Tukey FFT (BCL only, no external libraries).
/// Operates in place on parallel real/imaginary arrays whose length must be a power of two. Deterministic.
/// </summary>
public static class Fft
{
    /// <summary>True when <paramref name="n"/> is a positive power of two.</summary>
    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>
    /// In-place forward FFT. <paramref name="real"/> and <paramref name="imag"/> must have the same length,
    /// which must be a power of two. After the call they hold the transform (bins 0..N-1).
    /// </summary>
    public static void Forward(double[] real, double[] imag)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(imag);
        if (real.Length != imag.Length)
        {
            throw new ArgumentException("Real and imaginary arrays must have equal length.", nameof(imag));
        }

        int n = real.Length;
        if (n <= 1)
        {
            return;
        }

        if (!IsPowerOfTwo(n))
        {
            throw new ArgumentException($"FFT length must be a power of two, was {n}.", nameof(real));
        }

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j &= ~bit;
            }

            j |= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // Butterflies, doubling the sub-transform length each stage.
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wReal = Math.Cos(ang);
            double wImag = Math.Sin(ang);

            for (int i = 0; i < n; i += len)
            {
                double curReal = 1.0;
                double curImag = 0.0;
                int half = len >> 1;

                for (int k = 0; k < half; k++)
                {
                    int a = i + k;
                    int b = i + k + half;

                    double bReal = real[b] * curReal - imag[b] * curImag;
                    double bImag = real[b] * curImag + imag[b] * curReal;

                    real[b] = real[a] - bReal;
                    imag[b] = imag[a] - bImag;
                    real[a] += bReal;
                    imag[a] += bImag;

                    double nextReal = curReal * wReal - curImag * wImag;
                    curImag = curReal * wImag + curImag * wReal;
                    curReal = nextReal;
                }
            }
        }
    }
}
