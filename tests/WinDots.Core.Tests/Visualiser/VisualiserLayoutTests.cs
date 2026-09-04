using WinDots.Core.Visualiser;

namespace WinDots.Core.Tests.Visualiser;

public class VisualiserLayoutTests
{
    // Art-area styles at an art placement render in the artwork cell, never in the strip.
    [Theory]
    [InlineData(VisualiserStyle.Ring, VisualiserPlacement.BehindArt)]
    [InlineData(VisualiserStyle.Ring, VisualiserPlacement.UnderArt)]
    [InlineData(VisualiserStyle.Ring, VisualiserPlacement.OverArt)]
    [InlineData(VisualiserStyle.Halo, VisualiserPlacement.BehindArt)]
    [InlineData(VisualiserStyle.Particles, VisualiserPlacement.OverArt)]
    public void ArtStyleAtArtPlacementShowsInArtAreaOnly(VisualiserStyle style, VisualiserPlacement placement)
    {
        Assert.True(VisualiserLayout.ShowsInArtArea(style, placement));
        Assert.False(VisualiserLayout.ShowsInStrip(style, placement));
    }

    // Bottom placement moves an art-area style out of the artwork cell into the strip band — the setting is observable.
    [Theory]
    [InlineData(VisualiserStyle.Ring)]
    [InlineData(VisualiserStyle.Halo)]
    [InlineData(VisualiserStyle.Particles)]
    public void ArtStyleAtBottomMovesToStrip(VisualiserStyle style)
    {
        Assert.False(VisualiserLayout.ShowsInArtArea(style, VisualiserPlacement.Bottom));
        Assert.True(VisualiserLayout.ShowsInStrip(style, VisualiserPlacement.Bottom));
    }

    // Strip styles always sit in the bottom band regardless of placement, and never in the artwork cell.
    [Theory]
    [InlineData(VisualiserStyle.Bars, VisualiserPlacement.BehindArt)]
    [InlineData(VisualiserStyle.Bars, VisualiserPlacement.OverArt)]
    [InlineData(VisualiserStyle.Bars, VisualiserPlacement.Bottom)]
    [InlineData(VisualiserStyle.Waveform, VisualiserPlacement.UnderArt)]
    [InlineData(VisualiserStyle.Waveform, VisualiserPlacement.Bottom)]
    public void StripStyleAlwaysShowsInStrip(VisualiserStyle style, VisualiserPlacement placement)
    {
        Assert.True(VisualiserLayout.ShowsInStrip(style, placement));
        Assert.False(VisualiserLayout.ShowsInArtArea(style, placement));
    }

    // BlobPulse is drawn by the page (it scales the blob), so it renders in neither the art cell nor the strip.
    [Theory]
    [InlineData(VisualiserPlacement.BehindArt)]
    [InlineData(VisualiserPlacement.Bottom)]
    public void BlobPulseRendersInNeitherRegion(VisualiserPlacement placement)
    {
        Assert.False(VisualiserLayout.ShowsInArtArea(VisualiserStyle.BlobPulse, placement));
        Assert.False(VisualiserLayout.ShowsInStrip(VisualiserStyle.BlobPulse, placement));
    }

    // The three art placements yield three distinct depths relative to the blob (z=1) and dotted ring (z=0).
    [Fact]
    public void ArtPlacementsHaveDistinctZOrder()
    {
        int over = VisualiserLayout.ArtZIndex(VisualiserPlacement.OverArt);
        int under = VisualiserLayout.ArtZIndex(VisualiserPlacement.UnderArt);
        int behind = VisualiserLayout.ArtZIndex(VisualiserPlacement.BehindArt);

        Assert.True(over > 1, "over-art should sit above the album blob (z=1)");
        Assert.True(under < 1 && under >= 0, "under-art should sit between the dotted ring and the blob");
        Assert.True(behind < 0, "behind-art should sit behind the dotted ring and the blob");
        Assert.True(over > under && under > behind, "the three depths must be strictly ordered");
    }

    [Theory]
    [InlineData(VisualiserStyle.Ring, true)]
    [InlineData(VisualiserStyle.Halo, true)]
    [InlineData(VisualiserStyle.Particles, true)]
    [InlineData(VisualiserStyle.Bars, false)]
    [InlineData(VisualiserStyle.Waveform, false)]
    [InlineData(VisualiserStyle.BlobPulse, false)]
    public void IsArtStyleClassifiesFamilies(VisualiserStyle style, bool expected) =>
        Assert.Equal(expected, VisualiserLayout.IsArtStyle(style));

    [Theory]
    [InlineData(VisualiserStyle.Bars, true)]
    [InlineData(VisualiserStyle.Waveform, true)]
    [InlineData(VisualiserStyle.Ring, false)]
    [InlineData(VisualiserStyle.BlobPulse, false)]
    public void IsStripStyleClassifiesFamilies(VisualiserStyle style, bool expected) =>
        Assert.Equal(expected, VisualiserLayout.IsStripStyle(style));
}
