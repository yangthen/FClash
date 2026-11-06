# Windows WinUI 3 migration (unreleased preview)

## Features

- Added a C# / WinUI 3 native Windows client that communicates directly with the existing Go Core through private named pipes. Android and other platform UIs retain Flutter.
- Recreated the navigation rail, rounded dashboard/proxy/profile cards, connections/requests, logs, provider resources, tools, DNS/rules and settings layouts.
- Added read-only legacy SQLite/preferences/profile/script import into a separate native data directory, native ZIP backup and guarded restore.
- Preserved ordered DNS policies/servers and global/profile rules, manual delay tests, persisted country flags, selections and bounded page-scoped logs/request subscriptions.
- Added self-contained x64/ARM64 build scripts and native installer packaging; Windows setup routes to the native build.

## Bug Fixes

- Removed “close GUI, keep proxy running” from the Flutter settings and tray and removed Core's detached-session reconnect behavior. Desktop Core restores its owner's proxy recovery journal and stops when the owner disconnects, with a five-second shutdown deadline.
- Added framed RPC payload limits and pending-call cleanup on cancellation/disconnection.
- Native configuration compilation keeps displayed DNS/rule order, resolves MATCH policy placeholders and restores the previous active YAML on application failure.
- Native system proxy lifecycle records/restores the prior values and avoids overwriting a subsequent third-party proxy change.

## Validation and limitations

Validation results are recorded in `windows-native/validation.md`. This is not a published version/tag and does not move 0.8.96. Native migration coverage and remaining advanced-feature parity work are documented in `windows-native/README.md`. No production memory reduction is claimed without comparable measurements.
