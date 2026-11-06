# Native migration validation

## 2026-11-06 · framework removal

- The 11 native domain regression groups passed after moving translation input from ARB to shared JSON catalogs.
- x64 Release compilation and publish passed after deleting Flutter/Dart, the old Windows runner, plugins and other client platforms. Neither Windows build nor packaging invokes Flutter/Dart.
- `0.8.96-native-preview.1` ZIP and Inno installer were generated from the cleaned tree. The ZIP contains 743 entries, including seven checked application/Core/PRI/XAML/font/palette assets, and no Flutter/Dart files. Installer branding now comes from `resources/branding/app.ico`.
- Go Core tests passed. Existing published tags and previous packages were preserved. The installer was compiled, not installed on the host.

## 2026-11-06 · original interface restoration

- Native regression suite: 11 groups passed, including UI composition/column density, masonry bounds/no overlap, rule and DNS-policy order, legacy read-only import, persistence, framed RPC, configuration rollback, backup and WebDAV transport.
- x64 publish and ARM64 Release compilation passed. ARM64 compilation reported zero warnings and errors.
- Flutter Material palette export uses the project's existing seeds and content scheme; assets and licenses are included in native output.
- Safe-mode UI inspection covered the compact dashboard, Tools/Advanced/DNS hierarchy, bound DNS-policy title/detail rows, click-to-edit/cancel, parent navigation, proxy group/cards/cached flags and profile radio/menu placement. Scroll restoration was corrected after this inspection; its final interactive recheck remains pending.
- The final preview.3 published executable loaded the dashboard visual/accessibility tree using isolated dummy data. Its ZIP contains the application PRI, compiled XAML, fonts, palette, flags and Core; 12 required entries were checked byte-for-byte against the published output. The installer was compiled but was not installed on the host.
- The Flutter application/Core regression results from the initial migration are retained; this presentation pass changes native UI and packaging, not Flutter runtime behavior or Core behavior.

The original Flutter source, not the first native preview, is the presentation reference. [ui-parity.md](ui-parity.md) records the paths and layout rules. Host UI checks use dummy profiles under workspace-local `build/native-ui-parity-*` directories with `--safe-mode`; they do not start Core, attach to the user's application, register protocols/autorun or alter system proxy/DNS/TUN/helper services.

An early package failed at `MainWindow.InitializeComponent` because `dotnet publish` omitted compiled XAML and the application PRI index. The publish target now includes `.xbf` resources and the application `FClash.pri` explicitly, and packaging checks their presence along with font/palette assets. Do not deliver the intermediate preview.2 output.

No comparable production memory benchmark has been performed. Safe-mode UI memory cannot establish a production memory reduction. Real proxy/TUN, elevation, SSID changes and installer execution are outside this host verification. Native backups remain separate from Flutter ZIP backups; native data import copies legacy state read-only. The global script-library/editor workflow and some translation coverage still differ from Flutter.
