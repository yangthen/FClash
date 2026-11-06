# AGENTS.md

Read `.agents/project.md`, `.agents/commands.md` and `.agents/rules.md` before changes. Read `.agents/architecture.md` for platform/core work and the applicable `.agents/skills/*/SKILL.md` for UI/localization/core work.

## Supported product

- **FClash** supports **Windows and Android only**. Windows uses C# / WinUI 3 / .NET 10; Android uses Kotlin / Compose Material 3. Go/Mihomo supplies the Core.
- Do not introduce Flutter/Dart or restore other platform clients/build pipelines. Legacy preference identifiers and SQLite import are compatibility code, not runtime dependencies.
- Preserve the original Material visual/interaction contract: compact navigation, dashboard card order/spans, floating actions, Tools/Advanced/DNS hierarchy, selection/context menus, ordered DNS/rule editing, persistent flags and pull-to-test on Android. Framework migration must not redesign these flows.
- Keep UI separate from core transport, lifecycle, configuration compilation and persistence. See `windows-native/ui-parity.md` and `android/README.md`.
- Windows Core belongs to its GUI session; do not restore detached Core/reconnect or “close GUI, keep proxy running”. Android retains its independent VPN foreground service.

## Validation and safety

- Native logic: `dotnet run --project windows-native/FClash.Tests -c Release`; Android domain: `android/gradlew -p android :domain:test`.
- Compile Windows and Android after shared/build changes; run Android release lint when practical. Test compilation/order, migrations, rollback and lifecycle changes with meaningful regressions.
- Preserve legacy data read-only and migration recovery. Never hand-edit generated build outputs. Shared translations are JSON under `resources/translations`.
- Host app checks must use an isolated safe-mode data directory and must not change the user's proxy, DNS, TUN, helper or running app. Android safe-mode artifacts use the development package and may only be installed on a dedicated fresh test device. Stop only recorded test PIDs.

## Releases

- The current release is `0.8.97`; version metadata lives in `version.properties`. Preserve published `0.8.96`, `0.8.96-native-preview.1` and all existing tags/artifacts.
- Labels/tags use two final digits: `0.8.99 → 0.9.01 → ... → 0.9.99 → 0.10.01`. The `semver` property uses valid numeric components (`0.9.1`); Android `code` increases for every installable revision.
- Every release documents **Features**, **Bug Fixes**, validation and remaining limitations. Never claim memory/performance reductions without comparable measurements.
- Do not push tags or publish artifacts unless requested. Never move published tags.
- Android keeps `com.follow.fclash` and the existing independent release certificate. Keystores/passwords/local signing files never enter Git.
