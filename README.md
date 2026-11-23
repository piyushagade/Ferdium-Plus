# Ferdium Plus

A native Windows 11 desktop messaging and web-app aggregator built with **C# / WPF (.NET 8)**, **WinUI 3 (`WPF-UI`)**, and **Microsoft Edge WebView2** — designed for authentic Fluent 2 / Mica aesthetics and a minimal RAM/CPU footprint.

## Key Features
- **Authentic WinUI 3 / Windows 11 Shell**: Native DWM Mica backdrop, `FluentWindow`, System/Light/Dark themes, live accent color sync, and `Segoe UI Variable` typography.
- **Per-Service WebView2 Isolation & Multi-Account**: Run multiple accounts of the same service side-by-side with isolated profiles, custom labels, and corner badges.
- **Performance & Footprint Controls**: Lazy-load inactive services, Chromium `TrySuspendAsync` background throttling, configurable auto-hibernation, and cache/memory trimming on hibernate.
- **Per-Service Privacy Lock**: Passcode, Password, or **Windows Hello** (PIN / Fingerprint / Face) with TitleBar Quick-Lock (`Ctrl+Shift+L`), inactivity auto-lock, and re-lock on switch-away, minimize, or Windows sleep/lock.
- **Quick Switch (`Ctrl+K`) & Workspaces**: Instant keyboard-driven service switcher and workspace filtering.
- **Browser Extensions & System Tray**: Chrome/Edge extension support, granular mute/snooze controls, native Windows notifications, and system tray integration.

## Build & Run

```powershell
# Run unit tests
dotnet test

# Run locally
dotnet run --project FerdiumPlus

# Publish standalone win-x64 executable to dist/
dotnet publish FerdiumPlus/FerdiumPlus.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o dist
```

## Releases

Automated Windows x64 production builds and installers are generated via [`.github/workflows/release.yml`](.github/workflows/release.yml):

- **Tag-triggered release**: Push a version tag starting with `v` (e.g., `git tag v1.0.0 && git push origin v1.0.0`). GitHub Actions will run all unit tests, build the standalone portable executable and the Inno Setup installer, generate `SHA256SUMS.txt`, and publish a GitHub Release automatically. Tags containing a hyphen (e.g., `v1.0.0-beta.1`) are automatically marked as pre-releases.
- **Manual run**: Trigger the **Release** workflow from the GitHub Actions tab (`workflow_dispatch`) to produce downloadable build artifacts or optionally publish a release.

### Release Assets
- `FerdiumPlus-Setup-<version>-win-x64.exe` — Windows Setup installer (Inno Setup, per-user or all-users install, Start Menu / optional Desktop & Startup shortcuts)
- `FerdiumPlus-<version>-win-x64-portable.exe` — Standalone single-file portable executable
- `SHA256SUMS.txt` — SHA-256 checksums for all assets
