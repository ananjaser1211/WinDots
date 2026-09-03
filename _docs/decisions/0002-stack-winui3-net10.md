# ADR 0002 - Stack: C# / .NET 10, WinUI 3 on Windows App SDK 2.4.0, MSIX

- **Status**: Accepted, 2026-09-04

## Context

The product needs global media-session access (`Windows.Media.Control`), Core Audio COM, per-monitor DPI, composition animations, Acrylic, and a packaged restricted capability (`globalMediaControl`).

## Decision

- C# on .NET 10, TFM `net10.0-windows10.0.26100.0`.
- WinUI 3 via `Microsoft.WindowsAppSDK` **2.4.0** (latest stable at decision time), framework-dependent.
- Win32 interop through `Microsoft.Windows.CsWin32` source generation, scoped to window styles, DWM, Core Audio, and monitor APIs.
- MSIX packaging with the `globalMediaControl` capability.
- xunit for tests.

## Alternatives considered

- **WPF**: mature, but no first-class Acrylic/composition, weaker WinRT projection story, and packaging of restricted capabilities is clumsier.
- **Avalonia / Uno**: cross-platform overhead with no benefit for a Windows-only shell component; backdrop and DWM features would need custom interop anyway.
- **Electron / Tauri**: the memory and idle-CPU budgets in `07-testing-and-compatibility.md` are unreachable; no native media-session control.
- **C++/WinRT**: best control but slower iteration; C# keeps Core logic trivially unit-testable.

## Consequences

- Pin 2.4.0 and upgrade deliberately; avoid APIs deprecated in 2.x.
- Packaged-only development flow; unpackaged builds are not supported because the media capability requires package identity.
