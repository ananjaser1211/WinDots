# 01 - Reference study: the Caelestia dashboard

WinDots reproduces the *media drawer* of the Caelestia shell (Hyprland + Quickshell, GPL-3.0) as an independent Windows application. This document records what the reference does so that later work does not need to re-derive it. Nothing here is copied from Caelestia's source; it is an observational study of the screenshots in the repository root and of the public README configuration.

## Reveal interaction

- A thin handle sits at the top-centre of the monitor.
- The dashboard is revealed by dragging the handle downward (`dashboard.dragThreshold`, default 50 px) or by hovering when `dashboard.showOnHover` is enabled.
- The panel is a single wide surface with a tab strip along the top and content beneath.
- It closes by dragging upward, moving the pointer away, or an explicit dismiss.

## Tab strip (all three screenshots)

Four tabs, each an icon above a label: **Dashboard**, **Media**, **Performance**, **Weather**. The active tab has an accent-coloured label and a short underline. A thin divider separates the strip from the content.

WinDots keeps the strip from day one but ships only the Media tab in Phase A; other tabs are hidden until their phase lands (see `02-product-spec.md`).

## Media tab, playing (`MediaPlayer.png`)

Three columns on a dark surface with soft translucent blobs behind the content.

### Left: artwork

- Album art is clipped to a **wavy, scalloped blob** rather than a circle or rounded square.
- A **dotted ring** surrounds the blob. The dots are the track progress indicator: the elapsed portion of the ring is drawn in the accent colour, the rest in a muted tone.
- Ring and blob are the visual anchor of the panel and rotate/deform slightly as motion accents.

### Centre: metadata and transport

Top to bottom:

1. Title, large and bold (`The Outsider`).
2. Artist (`A Perfect Circle`).
3. Album or channel (`A Perfect Circle - Topic`, which is YouTube's uploader field; WinDots shows whatever the media session reports as album and falls back to the source name).
4. Seek row: elapsed `0:18`, thin track with an accent thumb, duration `4:06`.
5. Transport row: **shuffle** / **previous** / **play-pause** (a wide accent-filled pill, the primary action) / **next** / **repeat**. Secondary buttons are square-ish tonal buttons; shuffle and repeat are dimmed when inactive.
6. Volume row: mute/speaker icon, slider with thumb, percentage label (`29%`).

### Right: lyrics and player chooser

- Header: lyric icon + "Lyrics", with an overflow (three dots) button on the far right.
- Body: synced lyric lines; the current line is brighter and earlier lines are dimmed. Long content scrolls.
- Footer: a pill button naming the active player (`YouTube Music for Desktop`, truncated) with a chevron that opens a chooser of all discovered players.

### Background

- Large, low-contrast blob shapes float behind all columns, tinted from the artwork palette.
- The panel background is near-black; the accent is a desaturated teal derived from the artwork.

## Media tab, empty (`Static.png`)

Same layout with these state changes:

- Blob shows a placeholder "image + pause" glyph instead of artwork.
- Metadata reads `Unknown artist` / `Unknown album`; title is absent.
- Seek shows `0:00` / `0:00`, thumb at start.
- All transport buttons are visually disabled.
- Volume row is hidden.
- Lyrics panel shows a frown glyph and `No lyrics found`.
- Player chooser still shows the current source (`Chrome`), which is why the chooser stays visible even with no metadata.

This is the exact **Unknown/empty state** WinDots must implement.

## Dashboard tab (`Widgets.png`, later phase)

A card grid:

- **Weather card**: large condition icon, temperature (`35 C`), condition text (`Overcast`).
- **User card**: avatar, distro/OS chip, uptime chip (`up 7 hours, 28 minutes`).
- **Mini media card**: blob artwork with progress ring, title / album / artist, previous / play / next.
- **Clock**: stacked hour, separator dots, minutes, AM/PM.
- **Calendar**: month header with prev/next, weekday row, day grid with today highlighted.
- **Resource rings**: three radial gauges (CPU, memory, storage) with icons.
- **Mascot**: decorative image in the corner.

## Caelestia configuration surface mapped to WinDots settings

| Caelestia key | Default | WinDots equivalent (see `06-settings-schema.md`) |
|---|---|---|
| `dashboard.enabled` | `true` | `drawer.enabled` |
| `dashboard.showOnHover` | `true` | `drawer.showOnHover` (WinDots default `false`; hover-open on Windows steals pointer from title bars) |
| `dashboard.dragThreshold` | `50` | `drawer.dragThresholdPx` |
| `dashboard.mediaUpdateInterval` | `500` ms | `media.timelineTickMs` (interpolation tick only; GSMTC is event-driven) |
| `dashboard.resourceUpdateInterval` | `1000` ms | `performance.sampleIntervalMs` (Phase B/C) |
| `services.defaultPlayer` | `""` | `media.preferredPlayer` |
| `services.playerAliases` | `[]` | `media.playerAliases` |
| `services.lyricsBackend` | `"Auto"` | `lyrics.provider` (Phase C, default `"Off"`) |
| `services.visualiserBars` | `60` | `visualiser.bars` (Phase C) |
| `services.weatherLocation` | `""` | `weather.location` (Phase B/C, consent-gated) |
| `services.useFahrenheit` | `false` | `weather.useFahrenheit` |
| `appearance.font.scale` | `1` | `appearance.fontScale` |
| `appearance.deformScale` | `1` | `appearance.blobDeform` |

## Deliberately not reproduced

- Hyprland/Quickshell integration, workspace indicators, launcher, notifications, lock screen, bar.
- The OS/distro chip on the user card (replaced by Windows edition, or dropped).
- The mascot image (optional user-supplied image only; no bundled asset).
- MPRIS/`playerctl` behaviour; WinDots uses Windows media sessions.
- Any Caelestia QML, icons, fonts, or shaders.
