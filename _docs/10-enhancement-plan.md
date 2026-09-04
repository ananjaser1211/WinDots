# 10 - Enhancement plan (post-MVP integrations, polish, and roadmap catalogue)

Requested 2026-09-04 after M3/M5 landed. Items E1-E6 run as one integration phase after M4, then the visualiser phase. Every item ships with tests, docs, and a graphical polish pass (Segoe Fluent Icons only, never emoji; tokens only; motion per `04-visual-design.md`).

## Principles for anything that needs an account or a key

- **One click where the service allows it.** A "Sign in with <service>" button runs the whole flow: open the browser, wait for the callback or poll for approval, store the credential, show the signed-in state (avatar/name, "Sign out"). No copy-pasting tokens when the API offers an authorisation flow.
- **Keys never ship in the repository.** Services that require an app key (Last.fm, Spotify) read it from a build-time secret (`Directory.Build.props` property fed by an environment variable on the release machine) so official builds are one click. When the build has no key, the settings page shows a one-time "Create a key" helper that opens the provider's registration page with fields prefilled, accepts the pasted key once, validates it live, and then everything else is one click. Keys and sessions go to Windows Credential Manager through `ISecretStore`, never into `settings.json`.
- **Keyless first.** Prefer providers with no account (LRCLIB for lyrics, MusicBrainz for metadata, Discord local IPC for presence) so most users never see a sign-in.
- **Transparent.** The toggle text states exactly what leaves the device; `privacy.md` lists it.

## E1. Music-only detection and source rules

Problem: Windows media sessions include everything that publishes transport controls: local video, browser videos, meetings, games. WinDots is a music drawer.

- `WinDots.Core.Media.MusicDetector` (pure, unit-tested) scores a snapshot: `MediaKind.Music` +3; artist present +2; album present +2; duration 30 s to 20 min +1, above 45 min -3; video title patterns ("|", "Episode", "Trailer", "Live stream", "S01E") -2; source rule Always +10 / Never = excluded. Score >= 3 is music. The reason string feeds the chooser tooltip and diagnostics.
- Settings: `media.sourceMode`: `tracked` (default) | `all`; `media.sourceRules`: `{ match: "<AUMID or substring>", mode: "always" | "auto" | "never" }`. Built-in defaults: Spotify, Apple Music, Amazon Music, Tidal, Deezer, MusicBee, foobar2000, YouTube Music desktop clients -> always; Chrome/Edge/Firefox/Brave -> auto (the detector decides per session; YouTube Music tabs typically carry album + artist and pass); Windows Media Player -> auto; Teams, Zoom, Discord, VLC, mpv, Steam -> never.
- The coordinator excludes non-music sessions unless `sourceMode == all`; the chooser has an "All sources" toggle for a one-off look.
- Settings gains a **Sources** page: every source ever seen (persisted with last-seen time) with an Always / Auto / Never selector and the detector's last verdict.

## E2. Shuffle, repeat, media keys

- Shuffle and repeat already round-trip; polish: accent dot under the icon when active (A6), disabled when not advertised, tooltips naming the mode.
- Optional `media.captureMediaKeys` (off): register Play/Pause, Next, Previous, Stop as global hotkeys and route them to the active music session so a paused video in another window never steals the keys. Documented as opt-in because it overrides system routing.

## E3. Lyrics

- `ILyricsProvider` in Core: `LookupAsync(LyricsQuery(title, artists, album, duration), ct)` -> `LyricsResult` with plain lines, optional synced lines, provider name, attribution URL.
- Provider 1: **LRCLIB** (`https://lrclib.net/api/get`; keyless; attribution "Lyrics from LRCLIB"). HTTPS only, 5 s timeout, 256 KB cap, one request per track change, disk cache 30 days keyed by query hash. Provider 2 (later): NetEase/Musixmatch are licence-restricted; only add providers whose terms allow display.
- Panel: synced lines scroll with the interpolated position (current line accent, previous muted), plain lyrics scroll manually, "No lyrics found" placeholder otherwise. Offset control +/-500 ms in the overflow menu, persisted per track hash. `lyrics.provider`: `Off` (default) | `LRCLIB`; enabling shows what is sent.

## E4. Last.fm (one-click sign-in)

- Flow: "Sign in with Last.fm" -> `auth.getToken` -> open `https://www.last.fm/api/auth/?api_key=..&token=..` in the browser -> poll `auth.getSession` every 3 s for up to 5 min -> store session key in Credential Manager -> show username and avatar. "Sign out" deletes it.
- App key: build-time secret; without one, the "Create a key" helper opens `https://www.last.fm/api/account/create` with the application name prefilled and validates the pasted key/secret against `auth.getToken` before saving to Credential Manager.
- Now-playing on track start; scrobble at 50 % or 4 minutes for tracks over 30 s; dedupe by track identity; offline queue with backoff; love/unlove (heart glyph) in the overflow menu; recent tracks in the settings page. Core: `ScrobbleQualifier` state machine and `LastFmSigner` (md5) with tests.

## E5. Visualiser

- Capture: WASAPI loopback of the default render endpoint, 2048-sample FFT, up to 96 bands, attack/decay smoothing, silence detection; never written to disk. DSP in Core with tests; capture in `WinDots.Windows`; rendering in App via composition.
- Styles (`visualiser.style`): `bars` (bottom strip), `waveform` (thin line under the seek bar), `ring` (radial bars around the album blob, replacing the dotted ring while audio is active), `halo` (soft glow behind the blob following energy), `blobPulse` (blob amplitude follows energy), `particles` (sparse dots orbiting the blob at beat peaks). Placement (`visualiser.placement`): `underArt` | `overArt` | `behindArt` | `bottom`. Bars 24-96, smoothing, peak caps, colour from palette or fixed, mirrored option.
- Off under reduced motion, battery saver, remote session; opt-in (`visualiser.enabled` false).

## E6. Graphical polish (continuous)

- Segoe Fluent Icons only; hover/press/focus states on every control; 8 px rhythm; consistent corner radii.
- Chooser shows app icons resolved from the package or executable (Shell API), falling back to a glyph.
- Artwork cross-fade, palette transitions, high-DPI decode; empty and error states reviewed against `Static.png`.

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
| Update check | Check GitHub releases on demand and weekly (opt-in) | none | No auto-download |
| Shortcut conflict detection | Detect 1409 and offer alternatives / open Settings | none | PowerToys collision seen on the dev machine |
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
| `lastfm` | `enabled`, `scrobble`, `nowPlaying` | `false`, `true`, `true` |
| `visualiser` | `enabled`, `style`, `placement`, `bars`, `smoothing`, `mirrored` | `false`, `"ring"`, `"behindArt"`, `60`, `0.6`, `false` |
| `integrations` | `discordPresence`, `listenBrainz.enabled` | `false`, `false` |
