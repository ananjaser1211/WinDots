# WinDots documentation

`_docs/` is the detailed specification set for WinDots. `IMPLEMENTATION.md` at the repository root remains the top-level plan; the files here expand it into build-ready detail.

## Reading order

| # | File | Read it when |
|---|---|---|
| 1 | [01-reference-study.md](./01-reference-study.md) | You need to know what the Caelestia reference does and which parts WinDots reproduces |
| 2 | [02-product-spec.md](./02-product-spec.md) | You are adding or changing a user-facing feature |
| 3 | [03-ux-interaction-spec.md](./03-ux-interaction-spec.md) | You touch the handle, drawer gesture, keyboard, or dismissal behaviour |
| 4 | [04-visual-design.md](./04-visual-design.md) | You touch tokens, colour, artwork treatment, backdrop, or animation |
| 5 | [05-architecture.md](./05-architecture.md) | You add a project, contract, service, or Windows integration |
| 6 | [06-settings-schema.md](./06-settings-schema.md) | You add, rename, or default a setting |
| 7 | [07-testing-and-compatibility.md](./07-testing-and-compatibility.md) | You write tests or test against a real player |
| 8 | [08-roadmap.md](./08-roadmap.md) | You pick the next task or check milestone exit criteria |
| 9 | [09-dev-environment.md](./09-dev-environment.md) | You set up a machine or the build breaks on tooling |
| - | [decisions/](./decisions/) | You are about to make a hard-to-reverse choice; read existing ADRs and add one |

## Ownership and upkeep

- A behaviour change updates the spec that describes it in the same commit.
- A settings change updates `06-settings-schema.md` and the migration notes.
- A new real-player test result goes into the matrix in `07-testing-and-compatibility.md`.
- A consequential decision gets an ADR under `decisions/` using the template in `decisions/0001-license-gpl3.md`.

## Screenshots

The three PNGs at the repository root are the visual reference and are described region by region in `01-reference-study.md`:

- `MediaPlayer.png` - Media tab while a track plays.
- `Static.png` - Media tab with no usable session.
- `Widgets.png` - Dashboard tab (later phase).
