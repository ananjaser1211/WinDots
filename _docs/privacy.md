# Privacy and security

WinDots controls media locally. Nothing leaves the device unless an integration is switched on, and each integration says what it sends.

## Local by default

- Media control uses the Windows media-session API and Core Audio; no UI scraping, no private APIs.
- No telemetry, no crash upload, no usage statistics.
- Settings live in the package `LocalState\settings.json`. Artwork is cached under `LocalState\cache\artwork` with a 32 MB budget and 30-day expiry. The shell log under `LocalState\logs` contains states and reasons only, never titles, unless `diagnostics.includeMediaText` is enabled.
- Listening history is off until explicitly enabled and has retention, export, and delete-all controls.

## Integrations (all off by default)

| Integration | Sent when on | Credential |
|---|---|---|
| Lyrics (LRCLIB) | Title, artists, album, duration of the current track | none |
| Last.fm | Track metadata for now-playing and scrobbles; love/unlove | Session key in Windows Credential Manager after browser sign-in |
| ListenBrainz | Track metadata for listens | User token in Credential Manager |
| MusicBrainz | Track metadata for identifier lookup | none |
| Spotify Web API | Track identifiers for like/playlist actions | OAuth PKCE tokens in Credential Manager |
| Discord presence | Current track title/artist over the local Discord IPC pipe | none |

Rules for every network feature: HTTPS only, timeouts, bounded response sizes, MIME validation for artwork, rate limits respected, redaction of tokens and titles in logs, retry with backoff, and a one-click purge of any queued data.

## Secrets

Secrets are stored with Windows Credential Manager (or DPAPI) through `ISecretStore`, never in settings files or the repository. Release builds receive provider app keys from the build environment; source checkouts without keys still work with the in-app "Create a key" helper.

## Runtime

- The app never runs elevated.
- External URLs opened by the app are allow-listed provider pages or require confirmation.
- Artwork downloads enforce size and decode limits; malformed input is discarded.
