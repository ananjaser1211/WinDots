# 10 - Enhancement plan (post-MVP integrations, polish, and roadmap catalogue)

Requested 2026-09-04 after M3/M5 landed. Items E1-E6 run as one integration phase after M4, then the visualiser phase. Every item ships with tests, docs, and a graphical polish pass (Segoe Fluent Icons only, never emoji; tokens only; motion per `04-visual-design.md`).

## Principles for anything that needs an account or a key

- **One click where the service allows it.** A "Sign in with <service>" button runs the whole flow: open the browser, wait for the callback or poll for approval, store the credential, show the signed-in state (avatar/name, "Sign out"). No copy-pasting tokens when the API offers an authorisation flow.
- **Keys never ship in the repository.** Services that require an app key (Last.fm, Spotify) read it from a build-time secret (`Directory.Build.props` property fed by an environment variable on the release machine) so official builds are one click. When the build has no key, the settings page shows a one-time "Create a key" helper that opens the provider's registration page with fields prefilled, accepts the pasted key once, validates it live, and then everything else is one click. Keys and sessions go to Windows Credential Manager through `ISecretStore`, never into `settings.json`.
- **Keyless first.** Prefer providers with no account (LRCLIB for lyrics, MusicBrainz for metadata, Discord local IPC for presence) so most users never see a sign-in.
- **Transparent.** The toggle text states exactly what leaves the device; `privacy.md` lists it.

## E1. Music-only detection and source rules — BUILT (2026-09-04)

Problem: Windows media sessions include everything that publishes transport controls: local video, browser videos, meetings, games. WinDots is a music drawer.

Shipped: `WinDots.Core.Media.MusicDetector` (pure, `Score(MediaSnapshot, SourceRuleMode?) -> MusicVerdict`), `SourceRule`/`SourceMode`/`SourceRuleMode` with built-in defaults, `MediaOptions.SourceMode`/`SourceRules`/`RuleFor`, coordinator filtering (`Never` excluded; `Tracked` drops detector-rejected `Auto` sources; `Always` kept) with per-candidate `Verdicts` and a `ShowAllSources` runtime override, a bounded `SourceRegistry` persisted to `LocalState\sources.json`, the chooser "Show all sources" toggle with per-item verdict tooltips, and a settings Sources page. Tests in `MusicDetectorTests`, `SourceRegistryTests`, `SessionCoordinatorSourceRulesTests`.

- `WinDots.Core.Media.MusicDetector` (pure, unit-tested) scores a snapshot: `MediaKind.Music` +3; artist present +2; album present +2; duration 30 s to 20 min +1, above 45 min -3; video title patterns ("|", "Episode", "Trailer", "Live stream", "S01E") -2; source rule Always +10 / Never = excluded. Score >= 3 is music. The reason string feeds the chooser tooltip and diagnostics.
- Settings: `media.sourceMode`: `tracked` (default) | `all`; `media.sourceRules`: `{ match: "<AUMID or substring>", mode: "always" | "auto" | "never" }`. Built-in defaults: Spotify, Apple Music, Amazon Music, Tidal, Deezer, MusicBee, foobar2000, YouTube Music desktop clients -> always; Chrome/Edge/Firefox/Brave -> auto (the detector decides per session; YouTube Music tabs typically carry album + artist and pass); Windows Media Player -> auto; Teams, Zoom, Discord, VLC, mpv, Steam -> never.
- The coordinator excludes non-music sessions unless `sourceMode == all`; the chooser has an "All sources" toggle for a one-off look.
- Settings gains a **Sources** page: every source ever seen (persisted with last-seen time) with an Always / Auto / Never selector and the detector's last verdict.

## E2. Shuffle, repeat, media keys — BUILT (2026-09-04)

- Shuffle and repeat already round-trip; polish: accent dot under the icon when active (A6), disabled when not advertised, tooltips naming the mode. Shipped in `TransportBar` (4 px palette-accent `Ellipse` under the shuffle/repeat glyphs, `ToolTipService` captions "Shuffle: on/off" and "Repeat: off/list/track", buttons `IsEnabled=false` when the capability is not advertised).
- Optional `media.captureMediaKeys` (off): register Play/Pause, Next, Previous, Stop as global hotkeys and route them to the active music session so a paused video in another window never steals the keys. Documented as opt-in because it overrides system routing. Shipped in `ShellMessageWindow` (modifier-less `RegisterHotKey` on the media VKs, registered/unregistered live on the toggle, routed via `DrawerHost.MediaPlayPause/Next/Previous/Stop`; failures logged).

## E3. Lyrics — BUILT (2026-09-04)

Shipped — Core (`WinDots.Core.Lyrics`, BCL only, ~35 tests): `LyricsQuery`/`LyricsLine`/`LyricsResult`/`ILyricsProvider`; pure `LrcParser` ([mm:ss.xx], multiple timestamps per line, plain fallback); `LrclibProvider(HttpMessageHandler, log, timeout)` calling `GET https://lrclib.net/api/get` with the `WinDots/0.1` User-Agent, 5 s timeout, 256 KB cap, 404/errors -> null (other errors logged, redacted); `LyricsCache` (disk, 30 days, SHA-256 of the normalised query, ArtworkCache patterns, caches not-found too); pure `LyricsSync.CurrentIndex(lines, position, offset)`. App: `MediaViewModel` `LyricsLines`/`LyricsCurrentIndex`/`LyricsAttribution`/`LyricsSynced`/`LyricsState`, one lookup per track identity (cancel on change) only when `lyrics.provider == Lrclib`, index advanced on the timeline tick; `LyricsPanel` (accent current line, muted previous, auto-centre respecting reduced motion, plain lyrics scroll manually, "Lyrics from LRCLIB" caption); overflow "Enable lyrics" + offset +/-0.5 s/Reset persisted per track hash in `LocalState\lyrics-offsets.json`; Settings **Lyrics** page (provider Off/LRCLIB + privacy sentence + default offset). `DumpState` logs `lyricsState`.

- `ILyricsProvider` in Core: `LookupAsync(LyricsQuery(title, artists, album, duration), ct)` -> `LyricsResult` with plain lines, optional synced lines, provider name, attribution URL.
- Provider 1: **LRCLIB** (`https://lrclib.net/api/get`; keyless; attribution "Lyrics from LRCLIB"). HTTPS only, 5 s timeout, 256 KB cap, one request per track change, disk cache 30 days keyed by query hash. Provider 2 (later): NetEase/Musixmatch are licence-restricted; only add providers whose terms allow display.
- Panel: synced lines scroll with the interpolated position (current line accent, previous muted), plain lyrics scroll manually, "No lyrics found" placeholder otherwise. Offset control +/-500 ms in the overflow menu, persisted per track hash. `lyrics.provider`: `Off` (default) | `LRCLIB`; enabling shows what is sent.

## E4. Last.fm (one-click sign-in) — BUILT (2026-09-04)

Shipped — Core (`WinDots.Core.Scrobbling`, BCL only, ~32 tests): `LastFmSigner` (md5 of ordinal-sorted `name+value` + secret, `format`/`callback` excluded; known-vector tests); `LastFmClient(HttpMessageHandler, apiKey, secret)` with `auth.getToken`/`auth.getSession`, `track.updateNowPlaying`, `track.scrobble` (batch ≤ 50, indexed params), `track.love`/`unlove`, `user.getInfo`, `user.getRecentTracks` — JSON, HTTPS, 10 s timeout, 512 KB cap, GET reads / POST writes, error codes mapped to `LastFmException` (`IsTokenNotAuthorized`/`IsAuthFailure`/`IsTransient`); `ScrobbleQualifier` (50 % or 4 min, > 30 s only, dedupe per play, restart handling, pause-aware via min(wall, position) accumulation); `ScrobbleQueue` (disk JSON in LocalState, bounded 500, idempotent by identity+timestamp, exponential capped backoff, corruption tolerant). `TrackIdentity`/`Scrobble`/`LastFmSession`/`LastFmUserInfo`/`RecentTrack` models. Credentials via `ISecretStore` with `WinDots.Windows.Security.CredentialManagerSecretStore` (`PasswordVault`, resource `WinDots`). App: `LastFmKeys` reads the build-time key/secret from `[AssemblyMetadata]` (empty when unset); `LastFmService` watches the coordinator, sends now-playing on track start, qualifies + queues + drains scrobbles with backoff, love/unlove, pause-aware, redacted logs; a settings **Last.fm** page (enabled/scrobble/nowPlaying toggles, "Create a key" helper when no key, one-click "Sign in with Last.fm" with a 3 s / 5 min poll, progress ring + Cancel, username + avatar + recent tracks + "Sign out"); a heart button next to the title when signed in. Build secrets documented in `09-dev-environment.md`. Needs a Last.fm application key: official builds embed one via the `WinDotsLastFmApiKey`/`WinDotsLastFmSecret` environment variables; source checkouts use the in-app "Create a key" helper.

- Flow: "Sign in with Last.fm" -> `auth.getToken` -> open `https://www.last.fm/api/auth/?api_key=..&token=..` in the browser -> poll `auth.getSession` every 3 s for up to 5 min -> store session key in Credential Manager -> show username and avatar. "Sign out" deletes it.
- App key: build-time secret; without one, the "Create a key" helper opens `https://www.last.fm/api/account/create` and validates the pasted key/secret against `auth.getToken` before saving to Credential Manager.
- Now-playing on track start; scrobble at 50 % or 4 minutes for tracks over 30 s; dedupe by track identity; offline queue with backoff; love/unlove (heart glyph) next to the title; recent tracks in the settings page. Core: `ScrobbleQualifier` state machine and `LastFmSigner` (md5) with tests.

## E5. Visualiser — BUILT (2026-09-04)

Shipped — Core (`WinDots.Core.Visualiser`, BCL only, deterministic, ~35 tests): self-contained radix-2 iterative Cooley-Tukey `Fft`; `AudioSpectrum` (Hann window, power spectrum, 24-96 log-spaced bands over ~40 Hz-16 kHz, configurable dB reference/gain normalisation to 0..1, per-band fast-attack/slow-decay smoothing held across frames, optional peak-hold with decay, RMS silence detection that decays bands to zero); `WaveformBuffer` (min/max envelope downsample to M points in -1..1); `AudioMixer.DownmixToMono` (pure interleaved-stereo mix); `AudioSpectrumConfig` (+ `FromOptions`); `VisualiserStyle`/`VisualiserPlacement` enums and `VisualiserOptions` record. `IAudioLoopbackCapture` + `AudioFrame` contract in `WinDots.Core.Contracts` for the App to consume and tests to fake. Settings `visualiser` section (enabled/style/placement/bars/smoothing/mirrored) with `Settings.ToVisualiserOptions()`.

Capture — Core (`WinDots.Core.Visualiser.PcmConverter`, BCL only, ~8 tests): pure sample-format resolution (float32 / PCM16/24/32, `WAVE_FORMAT_EXTENSIBLE` sub-format) and raw-byte -> normalised-float conversion. Windows (`WinDots.Windows.Audio.WasapiLoopbackCapture` implements `IAudioLoopbackCapture`): WASAPI shared-mode loopback on the default render endpoint. All COM (device enumerator, endpoint, `IAudioClient`, `IAudioCaptureClient`) lives on one dedicated MTA capture thread (same single-thread rule as `MediaDispatcher`/`CoreAudioSessionProvider`); a 200 ms buffer is polled every ~10 ms (event-driven loopback is unreliable during silence), each packet converted via `PcmConverter` and raised as an interleaved `AudioFrame`; `AUDCLNT_BUFFERFLAGS_SILENT` packets emit zeros. `Start(ct)`/`Stop()`/`Dispose()` join the thread and release every COM object deterministically (`Marshal.FinalReleaseComObject`). Audio is analysed in memory only, never written to disk or transmitted.

App rendering — `Media/Controls/Visualiser` (composition-rendered `UserControl`, hit-test off): a fixed shape set whose composition transforms (or a polygon's points) are updated each frame, no per-frame layout or allocation. All six styles implemented (`blobPulse` scales the album blob from `MediaPage`; the rest draw in the control). `MediaViewModel` owns the DSP (`AudioSpectrum`/`WaveformBuffer` touched only on the UI thread), the capture-wanted gate, and bindable `VisualiserBands`/`VisualiserWaveform`/`VisualiserEnergy`/`VisualiserActive`/style/placement/bars/mirrored, pushed at ~60 Hz. `DrawerHost` owns the single `WasapiLoopbackCapture`, marshals `FrameAvailable` to the UI thread, and starts/stops it on the view-model's `VisualiserCaptureWantedChanged` — only while the drawer is open, a track is playing, `visualiser.enabled`, and the gates pass (reduced motion off, battery saver off via `PowerManager.EnergySaverStatus`, not a remote session); gates are re-resolved on each open and on settings change. Ring style hides the dotted progress ring while active. Settings window has a Visualiser section (enabled / style / placement / bar count 24-96 / smoothing / mirrored), applied live on Save. `DrawerHost.DumpState` logs visualiser state (enabled/style/placement/bars/gates/active/capturing) with no audio samples. Placement is fully wired (2026-09-04): the pure `VisualiserLayout` helper (Core, `Visualiser/VisualiserLayoutTests`) maps (style, placement) to the artwork-cell overlay or the bottom strip and gives the three art placements distinct `Canvas.ZIndex` depths, so all four placement options are observable; the redundant `Placement` DP was removed from the App `Visualiser` control (the page owns placement). Capture is now restart-safe: when `CaptureLoop` self-terminates (unsupported mix format, or a COMException from a device/default-endpoint change) it clears its thread field under the gate, so a later `Start()` spins up a fresh thread instead of no-opping forever. **Remaining**: default-render-device change re-attach (currently capture continues on the original endpoint until the next Stop/Start — documented follow-up); on-device visual tuning of sizes/energy scaling against the reference.

- Capture: WASAPI loopback of the default render endpoint, 2048-sample FFT, up to 96 bands, attack/decay smoothing, silence detection; never written to disk. DSP in Core with tests; capture in `WinDots.Windows`; rendering in App via composition.
- Styles (`visualiser.style`): `bars` (bottom strip), `waveform` (thin line under the seek bar), `ring` (radial bars around the album blob, replacing the dotted ring while audio is active), `halo` (soft glow behind the blob following energy), `blobPulse` (blob amplitude follows energy), `particles` (sparse dots orbiting the blob at beat peaks). Placement (`visualiser.placement`): `underArt` | `overArt` | `behindArt` | `bottom`. Bars 24-96, smoothing, peak caps, colour from palette or fixed, mirrored option.
- Off under reduced motion, battery saver, remote session; opt-in (`visualiser.enabled` false).

## E6. Graphical polish (continuous)

- Segoe Fluent Icons only; hover/press/focus states on every control; 8 px rhythm; consistent corner radii.
- Chooser shows app icons resolved from the package or executable (Shell API), falling back to a glyph.
- Artwork cross-fade, palette transitions, high-DPI decode; empty and error states reviewed against `Static.png`.

First pass — BUILT (2026-09-04): both the seek and volume `Slider`s now take their filled track and thumb from the per-artwork palette accent via a shared `WdAccentSliderStyle` (accent flows through the control's `Foreground`; unfilled track = `WdOutlineBrush`; PointerOver/Pressed/Disabled/focus states); high contrast is honoured through theme-mapped tokens (no system-brush overrides). Dotted ring track dots use a new raised-contrast `WdRingTrackBrush` so they read over acrylic in both themes (HC branch unchanged). Transport play/pause and tonal buttons keep their palette background through hover/press via a shared `WdAccentButtonStyle`/`WdTonalButtonStyle` (on-surface scrim overlay + a HC-only highlight border via `WdButtonHcBorderBrush`), aligned to the 8 px rhythm. Player chooser resolves the real per-app icon: Core contract `IAppIconProvider` (BCL-only, returns PNG/ICO bytes or null) + pure `AppIconKey` helper (unit-tested, +24 cases → 483 Core tests); `WinDots.Windows.AppIcons.AppIconProvider` resolves packaged AUMIDs via `AppInfo.DisplayInfo.GetLogo` and unpackaged ids via the running exe's Shell thumbnail, cached by normalised app id, off the UI thread, never throwing (null on failure), no disk/network; `PlayerChooserItem` gains an optional `ImageSource Icon`, decoded App-side at 48 px, shown in the flyout rows and the pill with a glyph fallback. **Pending on-device verification** (workstation was locked): confirm the accent sliders, ring contrast, and resolved chooser icons in a drawer capture.
- Deferred: the 40 px blob blur; on-device capture review against the reference screenshots.

## E7. Further enhancements (recorded for scheduling)

| Item | What | Sign-in | Notes |
|---|---|---|---|
| ListenBrainz | Listen submission + now-playing | User token pasted once from the profile page (no OAuth flow exists); "Open token page" button | Keyless read of stats |
| MusicBrainz / Cover Art Archive | Durable identifiers, missing artwork, disambiguation | none | Rate limit 1 req/s, user agent required |
| Spotify Web API | Like, add to playlist, queue, device transfer, canonical artwork | PKCE OAuth "Sign in with Spotify" (client id build-time secret) | Only when Spotify is the active source |
| Discord Rich Presence | Show the current track in Discord | none (local IPC pipe) | Opt-in; app id build-time secret |
| Now-playing toast | Small transient toast on track change with artwork | none | Respect focus assist and quiet hours |
| Mini strip | Pinned compact now-playing strip while the drawer is closed | none | Per-monitor, draggable |
| Wheel and middle-click on the handle | Volume / seek / play-pause | none | Settings-driven |
| Theme presets and import/export | Material, minimal, monochrome, high contrast; declarative theme files | none | Validated JSON |
| Listening history and insights | Opt-in local SQLite history, recently played, top artists, export/delete | none | DPAPI-protected optional encryption |
| Favourites | Local favourite flag; Last.fm love when connected | none | |
| Quiet hours and full-screen suppression | Hide handles during games/presentations | none | Already partly in settings |
| Update check | Check GitHub releases on demand and weekly (opt-in) | none | BUILT 2026-09-05: Core `WinDots.Core.Updates` (BCL only, +47 tests) — `SemanticVersion` (tolerant parser: optional `v`, 1–4 numeric core parts with the 4th ignored, `-prerelease`, `+build` ignored; SemVer precedence), `UpdateComparer`/`UpdateChecker` over an injected `IReleaseSource` (`ReleaseInfo`/`ReleaseFetch`/`UpdateResult`, pre-releases skipped unless the current build is a pre-release, every failure → `Error`); `WinDots.Windows.Updates.GitHubReleaseSource` (unauth read-only GET of `releases/latest`, User-Agent, 10 s timeout, 512 KB cap, graceful failures). App: `updates` settings section (`checkOnLaunch`, `lastCheckUtc`), a Settings **Updates** page ("Check for updates" button, inline status, "View release" link, auto-check toggle), and an opt-in weekly launch check in `DrawerHost` (throttled by `lastCheckUtc`, non-blocking, no download/install). |
| Shortcut conflict detection | Detect 1409 and offer alternatives / open Settings | none | BUILT 2026-09-05: Core `HotkeyRegistration.Classify` (1409→Conflict) + `ShortcutSuggester` (deterministic same-key alternatives, avoid-set + reserved-combo skip, +16 tests); `ShellMessageWindow` records the outcome and re-registers on settings change; Settings shows an inline conflict warning with "Suggest alternative" + "Open Windows keyboard settings" |
| Portable / unpackaged build | For users who cannot sideload MSIX | none | `globalMediaControl` requires identity; investigate sparse packages |
| Localisation | Resource-based strings, RTL | none | |
| Focus mode | Timer tied to a player, gentle track-change toast | none | |
| Ambient mode | Album-art screen for a secondary monitor | none | |
| Extension model | Out-of-process integrations over a versioned local protocol | none | After interfaces stabilise |

## Execution order

1. Integration workflow A (after M4): E1, E2, E3, E4.
2. Workflow B: E5 visualiser, E6 polish pass with capture review against the reference screenshots.
3. M6 packaging, then Phase B (Dashboard tab) and Phase C tabs, then E7 items by value.

## Settings additions (fold into `06-settings-schema.md` when built)

| Section | Key | Default |
|---|---|---|
| `media` | `sourceMode` | `"tracked"` |
| `media` | `sourceRules` | built-in list above |
| `media` | `captureMediaKeys` | `false` |
| `lyrics` | `provider` | `"Off"` |
| `lyrics` | `offsetMs` | `0` |
| `lastfm` | `enabled`, `scrobble`, `nowPlaying` | `false`, `true`, `true` — BUILT |
| `visualiser` | `enabled`, `style`, `placement`, `bars`, `smoothing`, `mirrored` | `false`, `"ring"`, `"behindArt"`, `60`, `0.6`, `false` |
| `integrations` | `discordPresence`, `listenBrainz.enabled` | `false`, `false` |
