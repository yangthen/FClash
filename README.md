# FClash

A simple, efficient native proxy client for Windows and Android, based on FLClash 0.8.94.

**English** · [简体中文](README_zh_CN.md)

- Windows: C# / .NET 10 / WinUI 3, using named pipes to communicate with a separate Go Core.
- Android: Kotlin / Jetpack Compose / Material 3, using Binder/AIDL to communicate with a separate VPN service and JNI to call the Go Core.

## Build

Initialize the `core/Clash.Meta` submodule first. Go 1.26.4 is required. Windows also requires .NET 10, Windows SDK 26100 and WinUI build tools. Android requires Python 3, JDK 21, SDK 36, Build-Tools 36.1.0, NDK 28.2.13676358 and CMake 3.22.1.

```powershell
dotnet run --project windows-native/FClash.Tests -c Release
./scripts/build_windows_native.ps1 -Architecture x64 -Label 0.8.97 -Targets zip
python scripts/build_android.py
```
