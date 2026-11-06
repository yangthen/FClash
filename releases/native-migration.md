# FClash native Windows / Android migration

Source preview tag: `0.8.96-native-preview.1`. See [versioned release notes](0.8.96-native-preview.1.md).

## Features

- Android UI and application logic use Kotlin / Compose Material 3 with the original mobile navigation, responsive dashboard, proxy group cards, profiles, Tools/Advanced/DNS hierarchy and page actions.
- Reuse the isolated Android VPN service and Go/JNI Core. Activity polling/event listeners follow lifecycle; quick tiles, boot/automatic start, per-app access control, SSID on-demand handling, subscriptions, scripts and backup/restore use native code.
- Preserve manual delay tests/pull-to-test and persistent per-profile country flags. DNS servers/policies and added rules support long-press dragging plus accessible up/down controls; their order is retained in generated YAML.
- Read-only import of same-package legacy SQLite/preferences/profile/script data into a separate native directory, plus native and legacy ZIP restore with bounded staging/recovery backups.
- Only Windows (WinUI 3) and Android (Kotlin/Compose) are supported. Version metadata, translations, Geo data and branding are framework-independent. Build entry points are PowerShell/.NET/Go and Python/Gradle/Go, without Flutter/Dart/Rust SDK dependencies.

## Bug Fixes

- Remove old Flutter/Dart UI, generated code, plugins, Linux/macOS clients, old runner/helper sources and obsolete multi-platform build/release configuration after native build verification.
- Preserve scalar types and rule/DNS order during Kotlin YAML compilation; prevent script access to Java classes and bound script instruction/time execution.
- Preserve existing Android signing/package identity, bump installable version code, and reject unsigned/development fallback when a release signing build is requested.
- Disable unsupported quick-tile components before Android 7, use Android-compatible list APIs, request coarse/fine location together and upgrade the transitive Fragment dependency for Activity Result compatibility.
- Keep UI state/scroll restoration scoped to the original pages and maintain dashboard actions in the toolbar.
- Restart the Android foreground service when live VPN/access-control/port options change, restore the previous configuration on failure, and report service startup failures instead of marking a rejected VPN as running.
- Accept both legacy array and mapping forms of DNS policies without changing their order. Remove automatic Crashlytics mapping/symbol uploads from native assembly.
- Publish observable native session state so Compose immediately reflects saved proxy layouts, selections, configuration edits and live statistics. Keep list-mode and tab-mode proxy navigation, including testing the group at the current scroll position.
- Read `config.json` from legacy ZIP staging as well as old on-device preferences, preserving the selected profile and DNS overrides during import.

## Validation

Native validation results and dedicated-device limitations are recorded in `android/validation.md` and `windows-native/validation.md`. Published release 0.8.96 and existing artifacts/tags are preserved. This source preview does not replace the stable release. No measured production memory reduction is claimed.
