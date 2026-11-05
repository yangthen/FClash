# AGENTS.md

This file is the entry point for AI coding agents working in this repository. Keep it small: detailed guidance lives under
`.agents/`, and discoverable repo skills live under `.agents/skills/*/SKILL.md`.

## Start Here

Read these files before making changes:

- [.agents/project.md](.agents/project.md): project overview, versions, and build dependencies.
- [.agents/commands.md](.agents/commands.md): build, development, code generation, and test commands.
- [.agents/rules.md](.agents/rules.md): lint, testing, generated-code, and workflow rules.

Read these only when the task touches their area:

- [.agents/architecture.md](.agents/architecture.md): core integration, providers, database, managers, build system, and
  local plugins.
- [.agents/agent-config.md](.agents/agent-config.md): how to choose between `AGENTS.md`, `.agents`, skills, Codex config,
  command rules, and hooks.
- [.agents/skills.md](.agents/skills.md): index of repo-scoped skills in `.agents/skills/`.

## Highest Priority Rules

- Use `flutter test`, not `dart test`, because models pull in Flutter types.
- Run code generation after modifying models, providers, or database schema.
- Do not manually edit generated files.
- Follow `analysis_options.yaml`, especially single quotes, trailing commas, `child:` last, no `print()`, const/final
  preferences, and declared return types.
- For CI parity, verify with `flutter pub get`, `flutter analyze --no-fatal-infos`, and
  `flutter test --reporter expanded` when practical.

## Repo Skills

Use repo skills from `.agents/skills/` when a task matches their descriptions. Current skills cover localization,
provider tests, UI work, and core/platform changes.

## Product Name

- The product is **FClash**. Use FClash for visible titles, package names, release artifacts, and release notes. Preserve legacy data directories, platform identifiers, and protocol aliases where needed for upgrades.

## Release Policy

- The current release target for this fork is `0.8.96`. Complete its changes and validation before creating the annotated release tag.
- Release labels and tags use two digits for the final component. After `0.8.99`, continue with `0.9.01`; after `0.9.99`, use `0.10.01`. Carry at 100 and restart the final component at 01.
- Dart pubspec versions must remain valid SemVer: release label `0.9.01` maps to pubspec `0.9.1`. Keep the label in release notes and the tag; never put an invalid leading-zero numeric component in pubspec.yaml.
- Every release must document **Features** and **Bug Fixes**, plus validation results and any remaining limitations. Do not claim measured performance improvements without comparable measurements.
- Do not push tags or publish release artifacts unless the user requests publication. Preserve existing tags; never move a published release tag.
- Host app verification must use an isolated safe-mode data directory and must not change the user's proxy, DNS, TUN, helper service, or running FlClash instance. Stop only test processes started by the agent, using their recorded PIDs.
