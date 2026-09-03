# ADR 0001 - License: GPL-3.0-or-later

- **Status**: Accepted, 2026-09-04
- **Deciders**: project owner

## Context

WinDots takes its interaction and visual language from the Caelestia shell, which is GPL-3.0. The README left the licence undecided, blocking dependency adoption and distribution.

## Decision

WinDots is licensed under GPL-3.0-or-later. The full text is in `LICENSE`.

## Consequences

- Licence-compatible with the reference project, so studying and describing its behaviour is unproblematic.
- WinDots is still an independent implementation: no Caelestia QML, shaders, icons, or assets are copied. The `AGENTS.md` prohibition stays in force because WinDots targets a different platform and copying would not be useful anyway.
- Third-party packages must be GPL-3.0-compatible (MIT, Apache-2.0, BSD, LGPL, MPL are fine). The Windows App SDK is MIT-licensed.
- Distributed builds must include the licence text and offer corresponding source.

## Alternatives considered

- MIT: more permissive, but the owner preferred copyleft alignment with the reference project.
- Defer: would have blocked dependency adoption.

## Template for future ADRs

Title, Status/date, Context, Decision, Consequences, Alternatives considered.
