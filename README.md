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
