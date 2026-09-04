# 04 - Visual design

All values are design tokens defined once in `WinDots.App/Resources/Tokens.xaml` and mirrored as C# constants in `WinDots.Core/Design/Tokens.cs` for tests. Views must reference tokens, never literals.

## Spacing and radius

| Token | Value |
|---|---|
| `Space.1` to `Space.6` | 4, 8, 12, 16, 24, 32 |
| `Radius.Small` | 8 |
| `Radius.Medium` | 16 |
| `Radius.Large` | 24 (drawer corners, bottom only) |
| `Radius.Pill` | 999 |

## Typography (Segoe UI Variable; fallback Segoe UI)

| Token | Size / weight | Use |
|---|---|---|
| `Type.Title` | 26 / SemiBold | Track title |
| `Type.Subtitle` | 18 / Regular | Artist |
| `Type.Body` | 16 / Regular | Album, lyrics, chooser |
| `Type.Caption` | 13 / Regular | Times, percentage, tab labels |

Scaled by `appearance.fontScale` and by Windows text scaling.

## Colour

Two static palettes plus a dynamic accent.

| Token | Dark | Light |
|---|---|---|
| `Surface` | #101416 | #F4F6F7 |
| `SurfaceRaised` | #1A1F22 | #FFFFFF |
| `OnSurface` | #E6EAEC | #1A1F22 |
| `OnSurfaceMuted` | #9AA3A7 | #5B6569 |
| `Outline` | #2C3336 | #D6DBDE |
| `AccentFallback` | #8FD3C8 | #1F7A6E |

### Artwork palette extraction (`IPaletteService`)

1. Downscale artwork to 64 x 64, ignore pixels with alpha < 128.
2. K-means (k = 5, 8 iterations, deterministic seed) in Oklab.
3. Candidate accent = cluster with the highest `chroma * sqrt(population)`; discard candidates with lightness outside [0.25, 0.85] in dark mode.
4. Adjust accent lightness until contrast against `Surface` >= 4.5:1 (WCAG AA) for text use and >= 3:1 for the play pill fill.
5. Derive: `Accent`, `OnAccent` (black or white by contrast), `AccentContainer` (accent at 18 % over Surface), `BlobTint` (accent at 8 %).
6. Fall back to `AccentFallback` when extraction fails or artwork is absent.
7. Result cached by artwork hash; transition 400 ms colour cross-fade.

## Artwork blob

- Path: superformula-style closed curve with 8 lobes, amplitude `0.06 * appearance.blobDeform`, generated once per size in `BlobGeometry.Create(size, lobes, amplitude, seed)` (Core, pure function, unit-tested).
- Rendered as a `CompositionGeometricClip` over the artwork `SpriteVisual`.
- Idle drift: the seed phase advances slowly (one full cycle per 20 s) unless reduced motion is on.

## Progress ring

- 72 dots, 3 px diameter at 100 %, on a circle 12 px outside the blob's bounding radius.
- Elapsed dots use `Accent`; remaining use `Outline`.
- Drawn as 72 small `SpriteVisual`s in a `ContainerVisual`, colour updated on each timeline tick (cheap; no per-frame layout).

## Collapsed handle (built, M4)

- The handle **window itself is the pill**: a small iPhone-home-indicator-style capsule, 112 x 12 logical at rest, 150 x 16 on hover, centred on the top edge. DWM rounds its corners (`DWMWA_WINDOW_CORNER_PREFERENCE`, anti-aliased and composited); it keeps `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` and a zero-height title bar (`ExtendsContentIntoTitleBar` + `SetTitleBar` on an empty element) so pointer input reaches the pill rather than being eaten as caption.
- **No `SetWindowRgn`**: a Win32 window region stops WinUI routing pointer input to the content (hover/click break), and its edges are aliased. The window is therefore the exact pill size; its height is floored by what WinUI will render and hit-test (a few-px window renders nothing).
- Animation: a slow autoreversing colour breathe at rest (`SineEase`, off under reduced motion); on hover an eased bounds tween grows the window and a `ColorAnimation` blooms the fill to the accent. On shrink the vacated screen strip is invalidated (`RedrawWindow`) so no edge residue lingers.
- Deferred: a truly hair-thin (~5 px) indicator would need a Win32 composition host (`DesktopWindowTarget` + `CompositionRoundedRectangleGeometry`) with manual hit-testing (codex-recommended), not attempted yet to keep the shell simple and stable.

## Background blobs (built, M4)

- Three large soft `BlobTint` ellipses (260 / 200 / 180 logical, opacity 0.5) placed behind the content at fixed relative anchors, drifting +/-12 px over 30 s on phase-offset composition Translation loops.
- Static positions under reduced motion, high contrast, or the opaque fallback; the whole canvas collapses when `appearance.backgroundBlobs` is off.
- Deferred: the 40 px Gaussian blur on each blob (drawn as flat soft ellipses for now).

## Backdrop (built, M4)

- `DesktopAcrylicController` with tint = `Surface` at 70 % and luminosity 0.9.
- Fallback to solid `Surface` when `UISettings.AdvancedEffectsEnabled` is false, on battery saver, over Remote Desktop, under high contrast, or when the controller fails; logs `backdrop: acrylic` / `backdrop: opaque (<reason>)`.
- Corners: `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)`.

## Artwork palette (built, M4)

- Artwork-derived palettes (`IPaletteService.FromArtwork` / `FromAccent` / `Fallback`) drive the accent, on-accent, accent-container, and blob-tint brushes with contrast floors; transitions animate over `Motion.Slow` (400 ms). `appearance.paletteSource` selects extracted, a fixed accent, or the token fallback.

## Motion tokens

| Token | Value |
|---|---|
| `Motion.Fast` | 120 ms, ease-out |
| `Motion.Base` | 250 ms, standard curve |
| `Motion.Slow` | 400 ms, standard curve |
| `Motion.Spring` | stiffness 320 / damping 28 |

Reduced motion maps every token to 100 ms linear and disables loops.

## High contrast

When `AccessibilitySettings.HighContrast` is true all colour tokens bind to `SystemColor*` brushes (a `HighContrast` ThemeDictionary in `Tokens.xaml`), the blob draws a 2 px `SystemColorWindowText` outline, the ring dots paint solid `WindowText`, and acrylic is off. Built in M4.

## Accessibility (built, M4)

- Every interactive control carries an `AutomationProperties.Name` (transport buttons, seek/volume sliders, mute, media tab, lyrics "More", player chooser, all settings controls; diagnostics buttons take their name from `Content`).
- The status caption is a live region (`LiveSetting="Assertive"`); play/pause transitions are announced through a separate hidden `Polite` live `TextBlock` ("Playing" / "Paused").
- At OS text scale > 150 % or a narrow width the body reflows via `LayoutStates` (VisualStateManager) to two rows — artwork + metadata above, lyrics below — inside the same 720 x 300 drawer, wrapped in a `ScrollViewer` so nothing is clipped.

## Iconography

Segoe Fluent Icons glyphs only: shuffle `E8B1`, previous `E892`, play `E768`, pause `E769`, next `E893`, repeat `E8EE`, mute `E74F`, volume `E767`, lyrics `E8D2`, more `E712`, chevron `E70D`. No third-party icon packs.

## Visualiser (E5, built)

The audio DSP lives in `WinDots.Core.Visualiser` and emits normalised band magnitudes (0..1) and a min/max waveform envelope (-1..1); the system audio it analyses is captured by `WinDots.Windows.Audio.WasapiLoopbackCapture` (WASAPI shared-mode loopback of the default render endpoint, in-memory only). Rendering is the App-side `Media/Controls/Visualiser` control: a composition-rendered `UserControl` (hit-test off) that keeps a fixed set of shapes and, each frame, updates only their composition transforms (scale / offset / opacity) or a polygon's point values — never layout and no per-frame allocation, the same discipline as `DottedProgressRing` and the background blobs. Styles: `bars` (rounded rects along the bottom, scale-Y from the band), `waveform` (a thin symmetric filled polygon), `ring` (radial `Line` spokes around the album blob, replacing the dotted ring while active), `halo` (a radial-gradient glow scaling/fading with overall energy), `blobPulse` (the page scales the album blob itself with energy; the control draws nothing), `particles` (a few dots orbiting the blob, brightening with energy). Placement is resolved by the host page (`MediaPage`) via the pure `WinDots.Core.Visualiser.VisualiserLayout` helper, which drives two `Visualiser` instances (one in the artwork cell, one in the bottom strip): for the art-area styles (ring/halo/particles) `underArt`/`overArt`/`behindArt` keep the overlay in the artwork cell at distinct z-depths (between the dotted ring and the blob, over the blob, or behind everything — set with `Canvas.ZIndex`), while `bottom` moves the same style into the bottom strip band. The strip styles (bars/waveform) always sit in the bottom band regardless of placement, and `blobPulse` ignores placement (the page scales the blob and the control draws nothing). The dotted progress ring is replaced by the `ring` style only while it occupies the artwork cell, not when the ring is placed in the strip. Colour comes from the artwork palette accent (fixed-accent when the palette source is fixed) — no new colour constants. Fast attack, slow decay (settings `smoothing` drives the decay; attack is derived faster) so the motion reads smoothly. The view-model runs the DSP on the UI thread (frames marshalled from the capture thread) and pushes bands/waveform/energy at ~60 Hz. Off entirely under reduced motion, battery saver, and remote sessions, and opt-in via `visualiser.enabled`.
