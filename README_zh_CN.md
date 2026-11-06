# FClash

基于 FLClash 0.8.94 的 Windows 和 Android 原生代理客户端，简洁，高效。

- Windows：C# / .NET 10 / WinUI 3，命名管道连接独立 Go Core。
- Android：Kotlin / Jetpack Compose / Material 3，Binder/AIDL 连接独立 VPN 服务，JNI 调用 Go Core。

## 构建

先初始化 `core/Clash.Meta` 子模块。需要 Go 1.26.4；Windows 还需要 .NET 10、Windows SDK 26100 和 WinUI 构建工具；Android 需要 Python 3、JDK 21、SDK 36、Build-Tools 36.1.0、NDK 28.2.13676358、CMake 3.22.1。

```powershell
dotnet run --project windows-native/FClash.Tests -c Release
./scripts/build_windows_native.ps1 -Architecture x64 -Label 0.8.97 -Targets zip
python scripts/build_android.py
```
