# FClash

Native Mihomo proxy clients for **Windows and Android**. Windows uses C# / WinUI 3 / .NET 10; Android uses Kotlin / Jetpack Compose / Material 3 with the existing isolated VPN service and Go/JNI Core. The product preserves the original visual and interaction design.

Builds require no Flutter, Dart or Rust SDK. See [中文说明](README_zh_CN.md) for the complete build, signing and migration workflow, [Windows](windows-native/README.md) and [Android](android/README.md) for platform details.

```powershell
dotnet run --project windows-native/FClash.Tests -c Release
./scripts/build_windows_native.ps1 -Architecture x64 -Label 0.8.96-native-preview -Targets zip
python scripts/build_android.py
```

Initialize `core/Clash.Meta` first. Go 1.26.4, .NET 10/Windows SDK 26100, and Python 3/JDK 21/Android SDK 36/NDK 28.2.13676358 are required for their respective targets. Keep the existing Android release signing key; never commit credentials. Version metadata comes from `version.properties`.

Host verification must use isolated safe-mode data and must not change system proxy, DNS, TUN or helper state. Native clients still need dedicated-device VPN/upgrade testing; no measured memory reduction is claimed. Published tags and existing release artifacts must remain unchanged.
