using WinDots.Core.Settings;

namespace WinDots.Core.Tests.Settings;

public class HotkeyRegistrationTests
{
    [Fact]
    public void Success_IsRegistered_WithZeroCode()
    {
        HotkeyRegistration result = HotkeyRegistration.Classify(succeeded: true, lastWin32Error: 0);

        Assert.Equal(HotkeyOutcome.Registered, result.Outcome);
        Assert.True(result.IsRegistered);
        Assert.False(result.IsConflict);
        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void Success_IgnoresAnyStaleError()
    {
        // A stale GetLastWin32Error must not turn a successful call into a failure.
        HotkeyRegistration result = HotkeyRegistration.Classify(succeeded: true, lastWin32Error: 1409);

        Assert.Equal(HotkeyOutcome.Registered, result.Outcome);
        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void Error1409_IsConflict_AndKeepsCode()
    {
        HotkeyRegistration result = HotkeyRegistration.Classify(succeeded: false, lastWin32Error: 1409);

        Assert.Equal(HotkeyOutcome.Conflict, result.Outcome);
        Assert.True(result.IsConflict);
        Assert.False(result.IsRegistered);
        Assert.Equal(1409, result.Code);
        Assert.Equal(1409, HotkeyRegistration.ErrorHotkeyAlreadyRegistered);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(1400)]
    [InlineData(87)]
    public void OtherErrors_AreFailed_AndKeepCode(int code)
    {
        HotkeyRegistration result = HotkeyRegistration.Classify(succeeded: false, lastWin32Error: code);

        Assert.Equal(HotkeyOutcome.Failed, result.Outcome);
        Assert.False(result.IsConflict);
        Assert.False(result.IsRegistered);
        Assert.Equal(code, result.Code);
    }
}
