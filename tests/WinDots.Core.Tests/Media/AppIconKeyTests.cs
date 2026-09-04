using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class AppIconKeyTests
{
    [Theory]
    [InlineData("Spotify.SpotifyMusic_zpdnekdrzrea0!Spotify", "spotify.spotifymusic_zpdnekdrzrea0!spotify")]
    [InlineData("  chrome.exe  ", "chrome.exe")]
    [InlineData("Foobar2000", "foobar2000")]
    public void NormalizeTrimsAndLowercases(string appId, string expected)
    {
        Assert.Equal(expected, AppIconKey.Normalize(appId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeReturnsEmptyForBlank(string? appId)
    {
        Assert.Equal(string.Empty, AppIconKey.Normalize(appId));
    }

    [Theory]
    [InlineData("Spotify.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", true)]
    [InlineData("chrome.exe", false)]
    [InlineData("foobar2000", false)]
    [InlineData("!Spotify", false)]
    [InlineData("", false)]
    public void IsPackagedDetectsAumid(string appId, bool expected)
    {
        Assert.Equal(expected, AppIconKey.IsPackaged(appId));
    }

    [Theory]
    [InlineData("Spotify.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify.SpotifyMusic_zpdnekdrzrea0")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!App", "Microsoft.ZuneMusic_8wekyb3d8bbwe")]
    public void PackageFamilyNameSplitsBeforeBang(string appId, string expected)
    {
        Assert.Equal(expected, AppIconKey.PackageFamilyName(appId));
    }

    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("foobar2000")]
    [InlineData("!Spotify")]
    [InlineData(null)]
    public void PackageFamilyNameIsNullForUnpackaged(string? appId)
    {
        Assert.Null(AppIconKey.PackageFamilyName(appId));
    }

    [Theory]
    [InlineData("chrome.exe", "chrome")]
    [InlineData("Chrome.EXE", "Chrome")]
    [InlineData("foobar2000", "foobar2000")]
    public void ExecutableNameStripsExeSuffix(string appId, string expected)
    {
        Assert.Equal(expected, AppIconKey.ExecutableName(appId));
    }

    [Theory]
    [InlineData("Spotify.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    [InlineData(null)]
    [InlineData("  ")]
    public void ExecutableNameIsNullForPackagedOrBlank(string? appId)
    {
        Assert.Null(AppIconKey.ExecutableName(appId));
    }
}
