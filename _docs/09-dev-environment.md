# 09 - Development environment

Findings on the primary workstation (2026-09-04):

| Component | State |
|---|---|
| Windows | 11 Home 10.0.26200 |
| .NET runtimes | 6.0.36, 8.0.29, 9.0.18, 10.0.10 |
| .NET SDK | 10.0.400 (installed 2026-09-04 via winget; pinned in `global.json`) |
| Visual Studio | Community 2022 17.14 (`D:\Program Files\Microsoft Visual Studio\17\Community`) plus Build Tools 2022 |
| Windows SDK | 10.0.26100 |
| winget | 1.29 |
| Git | installed |

## One-time setup

```powershell
# .NET 10 SDK
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
dotnet --version            # expect 10.0.x

# Visual Studio 2022 workloads (run the VS Installer): ".NET desktop development" and
# "Windows application development" (includes Windows App SDK / WinUI C# templates).

# Developer Mode for sideloading packaged debug builds
Start-Process ms-settings:developers
```

Pinned versions (central package management in `Directory.Packages.props`):

| Package | Version |
|---|---|
| `Microsoft.WindowsAppSDK` | 2.4.0 (stable, August 2026) |
| `Microsoft.Windows.SDK.BuildTools` | 10.0.28000.2705 |
| `Microsoft.Windows.CsWin32` | 0.3.333 (not yet referenced; added with the windowing milestone) |
| `xunit` / `xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk` | 2.9.3 / 4.0.0 / 18.9.0 |

The solution builds from the CLI alone; Visual Studio is optional. `WinDots.App` and `WinDots.Windows` declare `x64;ARM64` platforms, so always pass `-p:Platform=x64` (or ARM64).

Windows App SDK 2.x notes: `Window.Current`, `DependencyObject.Dispatcher`, and `FocusManager.GetFocusedElement()` are deprecated; use `DispatcherQueue` and `FocusManager.GetFocusedElement(XamlRoot)`.

## Everyday commands

```powershell
dotnet build WinDots.sln -c Debug -p:Platform=x64
dotnet test tests/WinDots.Core.Tests
dotnet test tests/WinDots.Core.Tests --filter "FullyQualifiedName~TimelineInterpolatorTests"
dotnet format --verify-no-changes
```

Platform tests need an interactive desktop and launch the fake player themselves:

```powershell
dotnet test tests/WinDots.Windows.Tests -p:Platform=x64
```

To drive a real player by hand, run `tests/WinDots.TestPlayer/bin/x64/Debug/net10.0-windows10.0.26100.0/WinDots.TestPlayer.exe` and type `play`, `pause`, `next`, `prev`, `seek 30`, `title Foo`, or `quit`. The real-player probe is described in `07-testing-and-compatibility.md`.

Run the packaged app from the CLI after a build (Developer Mode must be on; the build output is a loose layout, registered in place):

```powershell
Add-AppxPackage -Register src/WinDots.App/bin/x64/Debug/net10.0-windows10.0.26100.0/AppxManifest.xml -ForceUpdateFromAnyVersion
Start-Process "shell:AppsFolder\$((Get-AppxPackage -Name WinDots.Dev).PackageFamilyName)!App"
```

Alternatively open `WinDots.sln` in VS 2022, set `WinDots.App` as startup, and press F5. Uninstall the dev package with `Get-AppxPackage -Name WinDots.Dev | Remove-AppxPackage`.

## Build-time provider secrets

Provider app keys are never committed. Official builds embed them from the build environment; source checkouts build fine without them (the app shows an in-app "Create a key" helper instead). Currently used:

| Environment variable / MSBuild property | Consumed by | When unset |
|---|---|---|
| `WinDotsLastFmApiKey` | Embedded as `[assembly: AssemblyMetadata("WinDotsLastFmApiKey", ...)]` in `WinDots.App`; read at runtime by `LastFmKeys` | Empty; settings shows "Create a key" (paste + validate against `auth.getToken`) |
| `WinDotsLastFmSecret` | Same, as `WinDotsLastFmSecret` | Empty |

MSBuild imports environment variables as properties, so setting the variable before the build is enough:

```powershell
$env:WinDotsLastFmApiKey = "<key>"
$env:WinDotsLastFmSecret = "<secret>"
dotnet build WinDots.sln -c Release -p:Platform=x64
```

Keys entered through the in-app helper, and the session key + username after sign-in, are stored in Windows Credential Manager (resource `WinDots`) via `ISecretStore`, never on disk in the package or in `settings.json`.

## Environment hygiene

- Never commit `bin/`, `obj/`, `.vs/`, `*.pfx`, `AppPackages/` (already in `.gitignore`).
- Keep the dev signing certificate in the user store, not the repository.
