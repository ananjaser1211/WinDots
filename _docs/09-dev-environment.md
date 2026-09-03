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

`tests/WinDots.Windows.Tests` (platform tests) and `tests/WinDots.TestPlayer` are not scaffolded yet; see the roadmap.

Run the packaged app from the CLI after a build (Developer Mode must be on; the build output is a loose layout, registered in place):

```powershell
Add-AppxPackage -Register src/WinDots.App/bin/x64/Debug/net10.0-windows10.0.26100.0/AppxManifest.xml -ForceUpdateFromAnyVersion
Start-Process "shell:AppsFolder\$((Get-AppxPackage -Name WinDots.Dev).PackageFamilyName)!App"
```

Alternatively open `WinDots.sln` in VS 2022, set `WinDots.App` as startup, and press F5. Uninstall the dev package with `Get-AppxPackage -Name WinDots.Dev | Remove-AppxPackage`.

## Environment hygiene

- Never commit `bin/`, `obj/`, `.vs/`, `*.pfx`, `AppPackages/` (already in `.gitignore`).
- Keep the dev signing certificate in the user store, not the repository.
