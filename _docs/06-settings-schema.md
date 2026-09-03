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
  "lyrics": {},
  "visualiser": {},
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

## Later phases (present with defaults, ignored until the phase ships)

| Section | Key | Default |
|---|---|---|
| `lyrics` | `provider` | `"Off"` |
| `lyrics` | `offsetMs` | `0` |
| `visualiser` | `enabled` | `false` |
| `visualiser` | `bars` | `60` |
| `weather` | `enabled` | `false` |
| `weather` | `location` | `""` |
| `weather` | `useFahrenheit` | `false` |
| `performance` | `sampleIntervalMs` | `1000` |

## Migration and recovery

- `schemaVersion` bumps on any breaking rename. `SettingsMigrator` applies ordered steps; each step is unit-tested with a fixture file.
- On parse failure: rename the file to `settings.corrupt-<timestamp>.json`, load defaults, show a one-time notice.
- `settings.json.bak` is written before every save and restored automatically if the main file is unreadable.
- Secrets are never in this file. Integration tokens use `ISecretStore`.
