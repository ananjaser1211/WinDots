namespace WinDots.Core.Settings;

/// <summary>The outcome of a <c>RegisterHotKey</c> attempt, classified from its Win32 result.</summary>
public enum HotkeyOutcome
{
    /// <summary>The chord was registered and is live.</summary>
    Registered,

    /// <summary>Another app already owns the combination (Win32 <c>ERROR_HOTKEY_ALREADY_REGISTERED</c>, 1409).</summary>
    Conflict,

    /// <summary>Registration failed for some other reason; see <see cref="HotkeyRegistration.Code"/>.</summary>
    Failed,
}

/// <summary>
/// A pure classification of a <c>RegisterHotKey</c> result: the caller passes the success flag and the raw
/// <c>GetLastWin32Error</c> code, and this maps it to a <see cref="HotkeyOutcome"/> while keeping the raw code for
/// logging. BCL-only and side-effect free so it is unit-testable without any Win32 call.
/// </summary>
public readonly record struct HotkeyRegistration(HotkeyOutcome Outcome, int Code)
{
    /// <summary>Win32 <c>ERROR_HOTKEY_ALREADY_REGISTERED</c>: the combination is owned by another application.</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>True when the chord is registered and live.</summary>
    public bool IsRegistered => Outcome == HotkeyOutcome.Registered;

    /// <summary>True when another app owns the combination.</summary>
    public bool IsConflict => Outcome == HotkeyOutcome.Conflict;

    /// <summary>
    /// Classifies a <c>RegisterHotKey</c> result. <paramref name="succeeded"/> is the call's boolean return;
    /// <paramref name="lastWin32Error"/> is <c>GetLastWin32Error</c> captured immediately after a failure (ignored on
    /// success). Success maps to <see cref="HotkeyOutcome.Registered"/>, code 1409 to
    /// <see cref="HotkeyOutcome.Conflict"/>, and anything else to <see cref="HotkeyOutcome.Failed"/>.
    /// </summary>
    public static HotkeyRegistration Classify(bool succeeded, int lastWin32Error)
    {
        if (succeeded)
        {
            return new HotkeyRegistration(HotkeyOutcome.Registered, 0);
        }

        return lastWin32Error == ErrorHotkeyAlreadyRegistered
            ? new HotkeyRegistration(HotkeyOutcome.Conflict, lastWin32Error)
            : new HotkeyRegistration(HotkeyOutcome.Failed, lastWin32Error);
    }
}
