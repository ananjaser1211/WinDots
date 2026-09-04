using WinDots.Core.Scrobbling;

namespace WinDots.Core.Tests.Scrobbling;

public sealed class LastFmSignerTests
{
    [Fact]
    public void Sign_KnownVector_MatchesMd5()
    {
        var p = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = "KEY",
            ["method"] = "auth.getSession",
            ["token"] = "TOK",
        };

        // md5("api_keyKEYmethodauth.getSessiontokenTOKSECRET")
        Assert.Equal("0bf279e021f3a81b4553dd7e76cf72ad", LastFmSigner.Sign(p, "SECRET"));
    }

    [Fact]
    public void Sign_SortsKeysOrdinal_RegardlessOfInsertionOrder()
    {
        var p = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = "Hello World",
            ["method"] = "track.love",
            ["apiKey"] = "123",
        };

        // md5("apiKey123methodtrack.lovetitleHello WorldSHHH")
        Assert.Equal("1820807fc248dc4654e74de2200b1b97", LastFmSigner.Sign(p, "SHHH"));
    }

    [Fact]
    public void Sign_ExcludesFormatAndCallback()
    {
        var withExtras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = "KEY",
            ["method"] = "auth.getSession",
            ["token"] = "TOK",
            ["format"] = "json",
            ["callback"] = "cb",
        };
        var without = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = "KEY",
            ["method"] = "auth.getSession",
            ["token"] = "TOK",
        };

        Assert.Equal(LastFmSigner.Sign(without, "SECRET"), LastFmSigner.Sign(withExtras, "SECRET"));
    }

    [Fact]
    public void Sign_IsLowerCaseHex32()
    {
        string sig = LastFmSigner.Sign(new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "b" }, "s");
        Assert.Equal(32, sig.Length);
        Assert.Equal(sig, sig.ToLowerInvariant());
    }
}
