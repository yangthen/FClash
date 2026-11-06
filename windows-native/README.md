# FClash for Windows · WinUI 3 migration

Windows UI uses C# / WinUI 3 / .NET 10. It does not embed Flutter, a browser or WebView. Android uses Kotlin/Compose; Windows and Android are the only supported clients. The Go / Mihomo Core and existing framed JSON action protocol are reused.

## Build and verify

Install .NET 10, Visual Studio's Windows application development tools, Windows SDK 10.0.26100, and Go 1.26.4. NuGet restores the pinned Windows App SDK, SDK build tools, YamlDotNet, Microsoft.Data.Sqlite and Jint packages.

```powershell
dotnet run --project windows-native/FClash.Tests/FClash.Tests.csproj -c Release
dotnet build windows-native/FClash.WinUI/FClash.WinUI.csproj -c Release -p:Platform=x64
powershell -NoProfile -File scripts/build_windows_native.ps1 -Architecture x64 -Label 0.8.97 -Targets zip
```

For an installer use `-Targets 'exe,zip' -InnoCompiler C:/path/to/ISCC.exe`. ARM64 uses `-Architecture arm64`. Version metadata is read from `version.properties`; no Flutter/Dart SDK is needed. Artifacts go under `dist/windows-native/`; existing published 0.8.96 artifacts/tags are preserved. The installer has its own app identifier and destination, so it does not uninstall or overwrite the legacy Flutter installation.

Only launch isolated safe mode for host verification:

```powershell
build/native-windows/0.8.97/win-x64/FClash.exe --safe-mode --data-dir C:/absolute/workspace/build/native-safe-data
```

Safe mode does not start/discover/attach Core, modify proxy/DNS/TUN, register auto-start, elevate or access helper services. Configuration editing still operates inside the isolated data directory. Do not launch normal mode or run the installer on the developer's host as an automated smoke test. Record test PIDs and stop only those processes.

## Structure and data

- `FClash.Domain`: transport, configuration compilation, scripts, migration, persistence and backup; no dependency on WinUI.
- `FClash.WinUI`: XAML shell, native pages/settings, system proxy and notification icon; no Windows Forms or Rust UI bridge.
- `FClash.Tests`: executable regression suite, fails with a non-zero exit code; isolated temporary data, fake Core process, no external network calls or system network changes. Set `FCLASH_TEST_ROOT` to a workspace directory if temporary-directory access is restricted.

Native state lives under `%APPDATA%/com.follow/FClash/WinUI`. On first normal startup, legacy `%APPDATA%/com.follow/clash` or `.../FlClash` data is copied using a **read-only SQLite snapshot**, profile/script files and preferences. Originals remain untouched. Do not run both clients in normal mode on the same proxy ports. Selections, country flags, profile/rule order, DNS configuration and scripts are imported. New native changes are not written back to the Flutter database.

ZIP backups contain native state, profiles and scripts, excluding system proxy recovery journals and Core state. Restore checks path traversal and size limits and saves a recovery backup before replacing data. These native backups are a separate format from legacy Flutter backups.

## Lifecycle

The 0.8.95 **“close GUI, keep proxy running” feature is removed**, including the Flutter settings/tray actions and Core reconnection loop. Each GUI owns a unique pipe and Core. When the owner disconnects, Core first restores the native session's proxy journal, then shuts down with a five-second deadline. A normal exit restores the previous system proxy and stops Core. Closing a window can still minimize the **running GUI and tray**; choose tray Exit to stop the application.

System proxy updates keep an atomic recovery journal and restore only settings still owned by this client. On a GUI crash the Core performs recovery before exiting; on OS shutdown or a forced Core termination any remaining journal is recovered on the next normal launch. The native client does not control or install the legacy helper service. TUN uses the privileges of the native GUI; restart as administrator via Application settings if needed.

## Interface and migration coverage

The presentation contract is the existing Flutter interface, documented with source references in [ui-parity.md](ui-parity.md). The compact 80 px rail, 56 px page bar, 14 px card corners/gaps and original dashboard masonry packing are preserved. Speed curves, traffic donuts and radio outbound modes replace the first preview's large placeholder cards. Start/stop, delay test and profile import retain their page-specific floating actions; settings remain under Tools, with DNS/rules under Advanced. Material icons, Roboto and exported Material You palettes reuse the original assets/color generation. WinUI controls supply native accessibility/keyboard behavior. Dark/light themes are supported. DNS servers, domain policies and additional rules support drag and keyboard/button ordering, with the same order in compiled YAML. Successful country flags persist across restarts; only manual delay tests update them, and failed lookups retain the last flag.

WebDAV backup/restore, SSID/on-demand suspension, configurable global hotkeys, dashboard ordering/visibility and theme/language selection are included. Shared JSON catalogs supply the existing translations; new native descriptions are still being translated and may fall back to English or Chinese. SVG country flags use the bundled MIT-licensed flag-icons resources because Windows does not reliably shape flag Emoji.

The installer registers the `fclash://install-config` protocol. Application settings can optionally take over legacy `clash`, `clashmeta` and `flclash` schemes. Incoming links open a reviewable subscription form and are forwarded to the existing native GUI; they never attach an existing Core. Safe mode ignores protocol import requests and never registers schemes.

This is a **migration preview**, not a new stable release. WinUI controls differ in small layout/interaction details from the previous UI. Legacy ZIP backup conversion is not included; legacy data directories can be imported read-only. Subscription imports support Mihomo YAML. Real proxy/TUN/elevated-session/SSID checks require a dedicated test machine; they are deliberately not exercised against the developer's running proxy. Do not claim reduced production memory until a comparable workload has been measured.
