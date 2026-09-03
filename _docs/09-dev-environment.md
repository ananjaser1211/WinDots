# 09 - Development environment

Findings on the primary workstation (2026-09-04):

| Component | State |
|---|---|
| Windows | 11 Home 10.0.26200 |
| .NET runtimes | 6.0.36, 8.0.29, 9.0.18, 10.0.10 |
| .NET SDK | **not installed** |
| Visual Studio | 18 (2026), `C:\Program Files\Microsoft Visual Studio\18` |
| Windows SDK | 10.0.26100 |
| winget | 1.29 |
| Git | installed |

## One-time setup

```powershell
# .NET 10 SDK
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
dotnet --version            # expect 10.0.x

# Visual Studio 2026 workloads (run the VS Installer): ".NET desktop development" and
# "Windows application development" (includes Windows App SDK / WinUI C# templates).

# Developer Mode for sideloading packaged debug builds
Start-Process ms-settings:developers
```

Pinned versions (set in `Directory.Packages.props` once scaffolded):

| Package | Version |
|---|---|
| `Microsoft.WindowsAppSDK` | 2.4.0 (stable, August 2026) |
| `Microsoft.Windows.SDK.BuildTools` | latest matching 10.0.26100 |
| `Microsoft.Windows.CsWin32` | latest stable |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | latest stable |

Windows App SDK 2.x notes: `Window.Current`, `DependencyObject.Dispatcher`, and `FocusManager.GetFocusedElement()` are deprecated; use `DispatcherQueue` and `FocusManager.GetFocusedElement(XamlRoot)`.

## Everyday commands (valid once M1 scaffolding lands)

```powershell
dotnet build WinDots.sln -c Debug -p:Platform=x64
dotnet test tests/WinDots.Core.Tests
dotnet test tests/WinDots.Core.Tests --filter "FullyQualifiedName~DrawerControllerTests"
dotnet test tests/WinDots.Windows.Tests --filter "Category=Platform"   # needs desktop + TestPlayer
dotnet format --verify-no-changes
```

Run the packaged app: open `WinDots.sln` in VS 2026, set `WinDots.App` as startup, F5 (deploys the MSIX with the dev certificate). Command-line alternative after a build:

```powershell
Add-AppxPackage -Register src/WinDots.App/bin/x64/Debug/net10.0-windows10.0.26100.0/AppX/AppxManifest.xml
```

## Environment hygiene

- Never commit `bin/`, `obj/`, `.vs/`, `*.pfx`, `AppPackages/` (already in `.gitignore`).
- Keep the dev signing certificate in the user store, not the repository.
