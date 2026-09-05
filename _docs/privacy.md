# Privacy and security

WinDots controls media locally. Nothing leaves the device unless an integration is switched on, and each integration says what it sends.

## Local by default

- Media control uses the Windows media-session API and Core Audio; no UI scraping, no private APIs.
- No telemetry, no crash upload, no usage statistics.
- Settings live in the package `LocalState\settings.json`. Artwork is cached under `LocalState\cache\artwork` with a 32 MB budget and 30-day expiry. Lyrics, when enabled, are cached under `LocalState\cache\lyrics` (30-day expiry, keyed by a SHA-256 of the normalised query) and per-track sync offsets in `LocalState\lyrics-offsets.json`; neither is written while lyrics are off. The shell log under `LocalState\logs` contains states and reasons only, never titles, unless `diagnostics.includeMediaText` is enabled.
- Listening history is off until explicitly enabled and has retention, export, and delete-all controls.

## Integrations (all off by default)

| Integration | Sent when on | Credential |
|---|---|---|
| Lyrics (LRCLIB) | Title, artists, album, duration of the current track, sent to `https://lrclib.net/api/get` (HTTPS, 5 s timeout, 256 KB cap, one request per track change) | none |
| Last.fm | Artist, title, album, and duration of the current track, sent to `https://ws.audioscrobbler.com/2.0/` (HTTPS, 10 s timeout, 512 KB cap) for now-playing (track start) and scrobbles (at 50 % or 4 min, tracks over 30 s); love/unlove on request. Pending scrobbles are queued in `LocalState\scrobble-queue.json` and retried with backoff. | API key/secret (only when the build embeds none) and the session key + username in Windows Credential Manager (`WinDots` resource) after browser sign-in; "Sign out" deletes the session |
| ListenBrainz | Track metadata for listens | User token in Credential Manager |
| MusicBrainz | Track metadata for identifier lookup | none |
| Spotify Web API | Track identifiers for like/playlist actions | OAuth PKCE tokens in Credential Manager |
| Discord presence | Current track title/artist over the local Discord IPC pipe | none |
| Update check | Nothing is sent about you: a read-only, unauthenticated HTTPS GET of `https://api.github.com/repos/AnanJaser1211/WinDots/releases/latest` (10 s timeout, 512 KB cap) to compare the running version against the latest release tag. Off by default; runs only when you press "Check for updates" or opt into the weekly launch check (`updates.checkOnLaunch`, throttled to once per week via `updates.lastCheckUtc`). No auto-download, no auto-install, no telemetry; a newer release only offers a link to its GitHub page. | none |

Rules for every network feature: HTTPS only, timeouts, bounded response sizes, MIME validation for artwork, rate limits respected, redaction of tokens and titles in logs, retry with backoff, and a one-click purge of any queued data.

## Secrets

Secrets are stored with Windows Credential Manager (or DPAPI) through `ISecretStore`, never in settings files or the repository. Release builds receive provider app keys from the build environment; source checkouts without keys still work with the in-app "Create a key" helper.

## Runtime

- The app never runs elevated.
- External URLs opened by the app are allow-listed provider pages or require confirmation.
- Artwork downloads enforce size and decode limits; malformed input is discarded.
