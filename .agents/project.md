# Project
FClash supports Windows and Android only. Windows: C# / WinUI 3 / .NET 10 under `windows-native/`. Android: Kotlin / Compose Material 3 and native VPN/JNI modules under `android/`. Shared Go/Mihomo Core: `core/`, submodule `core/Clash.Meta`.
Version metadata: `version.properties`; shared data: `resources/` (translations, branding, Geo data). No Flutter, Dart or Rust SDK is required.
Dependencies: Go 1.26.4; Windows .NET 10, Windows SDK 26100/WinUI build tools, Inno Setup; Android Python 3, JDK 21, SDK 36, Build-Tools 36.1.0, NDK 28.2.13676358, CMake 3.22.1.
