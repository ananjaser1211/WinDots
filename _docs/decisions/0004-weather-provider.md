# ADR 0004 - Weather provider and location source

- **Status**: Accepted, 2026-09-05 (product decision taken with the developer during the Phase C sweep).

## Context

The Dashboard (and the Weather tab in Phase C) show current conditions and a short forecast. Weather needs a network provider and a location. Both are privacy-relevant: `_docs/10-enhancement-plan.md` requires keyless providers where possible and that anything leaving the device is consent-gated and listed in `privacy.md`. The dashboard card already ships as a consent placeholder (`weather.consentGranted`, default `false`).

## Decision

- **Provider: Open-Meteo** (`https://api.open-meteo.com/v1/forecast`, geocoding via `https://geocoding-api.open-meteo.com/v1/search`). Keyless, no account, free for non-commercial use, HTTPS only, JSON. Attribution "Weather data by Open-Meteo" shown on the card.
- **Location: typed place name by default.** The user types a city in Settings; it is geocoded once through the Open-Meteo geocoding endpoint and the resolved latitude/longitude (rounded to two decimals, ~1 km) plus display name are stored in `settings.json`. Only the coordinates are sent on each forecast request.
- **Optional device location.** A separate "Use device location" toggle uses `Windows.Devices.Geolocation.Geolocator`; Windows shows its own OS permission prompt. Coordinates are rounded to two decimals before use and never persisted beyond the current session unless the user pins them.
- **Consent gate.** No request is made until `weather.consentGranted` is true; the consent copy states exactly what is sent (coordinates only, no identifiers). Turning consent off clears cached weather.
- **Cadence and resilience.** Refresh at most every 15 minutes while the drawer is open (`weather.refreshMinutes`, clamped 10-120), 10 s timeout, 256 KB cap, one in-flight request, exponential backoff on failure, last good result cached in memory with its timestamp and shown as stale after 2 h. Offline shows the last value with a muted "updated N min ago".
- **Units** follow `weather.units` (`auto` from the OS region, `metric`, `imperial`).

## Alternatives considered

- **OpenWeatherMap / WeatherAPI**: need an API key per user or a shipped key; rejected under the keyless-first principle.
- **MSN/Windows weather feed**: undocumented and licence-unclear; rejected.
- **Device location only**: simpler, but users without location services (desktops) would have no weather; typed city keeps the feature available everywhere.

## Consequences

- New Core contract `IWeatherProvider` (BCL-only) with a fake for tests; HTTP implementation in `WinDots.Windows` (or App) following the LRCLIB/GitHub adapter idiom.
- `privacy.md` gains the two Open-Meteo endpoints, what is sent (coordinates, units), and the consent switch.
- Settings gains `weather.{consentGranted, placeName, latitude, longitude, useDeviceLocation, units, refreshMinutes}`; schema additive, no migration.
