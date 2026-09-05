# 06 - Settings schema

File: `%LOCALAPPDATA%\WinDots\settings.json`. UTF-8, camelCase, written atomically. Unknown keys are preserved on round-trip; invalid values fall back to defaults with a logged warning.

```json
{
  "schemaVersion": 1,
  "drawer": {},
  "media": {},
  "appearance": {},
  "monitors": {},
  "privacy": {},
  "diagnostics": {},
  "lyrics": { "provider": "Lrclib", "offsetMs": 0 },
  "lastfm": { "enabled": false, "scrobble": true, "nowPlaying": true },
  "visualiser": { "enabled": false, "style": "ring", "placement": "behindArt", "bars": 60, "smoothing": 0.6, "mirrored": false },
  "weather": {},
  "performance": {}
}
```

## `drawer`

| Key | Type | Default | Notes |
|---|---|---|---|
| `enabled` | bool | `true` | Master switch for handles |
| `showOnHover` | bool | `false` | Open after `hoverOpenDelayMs` hovering the handle |
| `hoverOpenDelayMs` | int | `600` | |
| `dragThresholdPx` | int | `50` | Press-to-drag threshold |
| `openThreshold` | double | `0.35` | Progress needed on release |
| `velocityThresholdPxPerS` | int | `600` | |
| `toggleShortcut` | string | `"Win+Shift+M"` | Parsed by `ShortcutParser` |
| `autoHideMs` | int | `0` | 0 = never |
| `hideAfterCommand` | bool | `false` | |
| `hideInFullscreen` | bool | `true` | |
| `alwaysOnTop` | bool | `true` | Drawer stays topmost while open |
| `width` / `height` | int | `720` / `300` | Logical px |

## `media`

| Key | Type | Default | Notes |
|---|---|---|---|
| `preferredPlayer` | string | `""` | AUMID or exe name; scored +400 |
| `ignoredPlayers` | string[] | `[]` | Never shown |
| `playerAliases` | object | `{}` | e.g. `{ "Spotify.exe": "Spotify" }` |
| `timelineTickMs` | int | `500` | Interpolation tick while playing and open |
| `allowSharedVolume` | bool | `false` | Show volume for `Medium` matches |
| `seekStepS` | int | `5` | Arrow-key seek |
| `volumeStepPercent` | int | `2` | |
| `sourceMode` | `"tracked"`, `"all"` | `"tracked"` | `tracked` shows only music (Always rules and Auto rules the detector accepts); `all` shows every source except `never` |
| `sourceRules` | `{ match, mode }[]` | built-in defaults | `match` is an AUMID or case-insensitive substring; `mode` is `always` \| `auto` \| `never`. User rules take precedence; the built-ins (Spotify/Apple Music/Amazon Music/Tidal/Deezer/MusicBee/foobar2000/YouTube Music → `always`; Chrome/Edge/Firefox/Brave/Media Player → `auto`; Teams/Zoom/Discord/VLC/mpv/Steam → `never`) are appended as fallback |
| `captureMediaKeys` | bool | `false` | Capture Play/Pause, Next, Previous, Stop as global hotkeys and route them to the active music session. Opt-in because it overrides system routing |

Sources seen at runtime are recorded (app id, display name, last-seen time, last verdict) in `LocalState\sources.json`, bounded to 200 entries, and drive the settings Sources page. This file is not part of `settings.json`.

## `appearance`

| Key | Type | Default |
|---|---|---|
| `theme` | `"auto"`, `"dark"`, `"light"` | `"auto"` |
| `backdrop` | `"acrylic"`, `"opaque"` | `"acrylic"` |
| `fontScale` | double | `1.0` |
| `blobDeform` | double | `1.0` |
| `paletteSource` | `"artwork"`, `"fixed"` | `"artwork"` |
| `fixedAccent` | string | `"#8FD3C8"` |
| `reduceMotion` | `"system"`, `"on"`, `"off"` | `"system"` |
| `backgroundBlobs` | bool | `true` |

## `monitors`

| Key | Type | Default |
|---|---|---|
| `mode` | `"all"`, `"primary"`, `"list"` | `"all"` |
| `enabledDeviceIds` | string[] | `[]` |
| `handleOffsetPercent` | int | `50` (horizontal position along the top edge) |

## `privacy`

| Key | Type | Default |
|---|---|---|
| `historyEnabled` | bool | `false` |
| `historyRetentionDays` | int | `90` |
| `networkFeaturesAcknowledged` | bool | `false` |

## `diagnostics`

| Key | Type | Default |
|---|---|---|
| `logLevel` | `"warning"`, `"info"`, `"debug"` | `"warning"` |
| `includeMediaText` | bool | `false` |

## `lyrics`

| Key | Type | Default | Notes |
|---|---|---|---|
| `provider` | `"Off"`, `"Lrclib"` | `"Lrclib"` | On by default; the settings toggle turns it off. `Off` sends nothing to the network. `Lrclib` looks up lyrics (keyless) by title, artist, album, and duration over HTTPS; the source is not shown in the UI. See `_docs/privacy.md`. |
| `offsetMs` | int | `0` | Default synchronisation offset in milliseconds (positive advances lines earlier). Per-track overrides are stored separately in `LocalState\lyrics-offsets.json`, not in settings. |

Fetched lyrics are cached for 30 days in `LocalState\cache\lyrics`, keyed by a SHA-256 of the normalised query; both found and not-found answers are cached so a track is queried at most once.

## `lastfm`

| Key | Type | Default | Notes |
|---|---|---|---|
| `enabled` | bool | `false` | Master switch. Nothing is sent while off or signed out. |
| `scrobble` | bool | `true` | Submit qualified plays (50 % or 4 min, tracks over 30 s) as scrobbles. |
| `nowPlaying` | bool | `true` | Send a now-playing notification on track start. |

The JSON section key is `lastfm`. Credentials (API key/secret when the build has none, and the session key + username after sign-in) are stored in Windows Credential Manager under the `WinDots` resource via `ISecretStore`, never in `settings.json`. Pending scrobbles are queued in `LocalState\scrobble-queue.json` (bounded to 500, idempotent by track identity + timestamp, retried with exponential backoff); this file is not part of `settings.json`. See `_docs/10-enhancement-plan.md` (E4), `_docs/privacy.md`, and `_docs/09-dev-environment.md` (build-time key).

## `visualiser`

| Key | Type | Default | Notes |
|---|---|---|---|
| `enabled` | bool | `false` | Master switch. Off under reduced motion, battery saver, and remote sessions regardless. |
| `style` | enum | `"ring"` | `bars` \| `waveform` \| `ring` \| `halo` \| `blobPulse` \| `particles` (serialised camelCase). |
| `placement` | enum | `"behindArt"` | `underArt` \| `overArt` \| `behindArt` \| `bottom`. For art-area styles (ring/halo/particles) the three art placements set the depth in the artwork cell (over / between / behind the album blob) and `bottom` moves the style into the bottom strip band. Strip styles (bars/waveform) always render in the strip; `blobPulse` ignores placement (the page scales the blob). |
| `bars` | int | `60` | Frequency bands; clamped to 24..96. |
| `smoothing` | double | `0.6` | Decay smoothing 0..1; higher is smoother (slower fall). Attack is always faster. |
| `mirrored` | bool | `false` | Mirror the bands about the centre. |

The DSP is in `WinDots.Core.Visualiser` (`AudioSpectrum`, `WaveformBuffer`, `AudioMixer`, `Fft`, `PcmConverter`, pure and unit-tested); `Settings.ToVisualiserOptions()` bridges this section to the runtime `VisualiserOptions`. Loopback audio is captured via `IAudioLoopbackCapture` (Core contract), implemented by `WinDots.Windows.Audio.WasapiLoopbackCapture` (WASAPI shared-mode loopback on the default render endpoint), and never written to disk. See `_docs/10-enhancement-plan.md` (E5).

## `updates`

| Key | Type | Default | Notes |
|---|---|---|---|
| `checkOnLaunch` | bool | `false` | Opt-in: run a background update check at launch, throttled to once per week. Nothing is fetched while off. |
| `lastCheckUtc` | datetime? | `null` | UTC timestamp of the last completed check, used to throttle the weekly check. Written by the app, not user-editable. |

The update check is a read-only, unauthenticated HTTPS GET of the GitHub releases API (`https://api.github.com/repos/AnanJaser1211/WinDots/releases/latest`); it compares the running package version against the latest release tag and, when newer, offers a link to the release page. No auto-download, no auto-install, no telemetry. The fetch abstraction (`IReleaseSource`) and comparison logic (`SemanticVersion`, `UpdateComparer`, `UpdateChecker`) live in `WinDots.Core.Updates` (BCL only, unit-tested); the HTTP implementation is `WinDots.Windows.Updates.GitHubReleaseSource`. See `_docs/10-enhancement-plan.md` (E7) and `_docs/privacy.md`.

## Later phases (present with defaults, ignored until the phase ships)

| Section | Key | Default |
|---|---|---|
| `weather` | `enabled` | `false` |
| `weather` | `location` | `""` |
| `weather` | `useFahrenheit` | `false` |
| `performance` | `sampleIntervalMs` | `1000` |

## Migration and recovery

- `schemaVersion` bumps on any breaking rename. `SettingsMigrator` applies ordered steps; each step is unit-tested with a fixture file.
- On parse failure: rename the file to `settings.corrupt-<timestamp>.json`, load defaults, show a one-time notice.
- `settings.json.bak` is written before every save and restored automatically if the main file is unreadable.
- Secrets are never in this file. Integration tokens use `ISecretStore`.
