# FClash

基于 Mihomo 的 Windows 和 Android 原生代理客户端，保留原有 Material 风格与操作路径。

- Windows：C# / .NET 10 / WinUI 3，命名管道连接独立 Go Core。
- Android：Kotlin / Jetpack Compose / Material 3，Binder/AIDL 连接独立 VPN 服务，JNI 调用 Go Core。
- 仅维护这两个平台；构建不需要 Flutter、Dart 或 Rust 工具链。
- Android 包名 `com.follow.fclash`，覆盖更新须使用已有 FClash 发布签名。

## 构建

先初始化 `core/Clash.Meta` 子模块。需要 Go 1.26.4；Windows 还需要 .NET 10、Windows SDK 26100 和 WinUI 构建工具；Android 需要 Python 3、JDK 21、SDK 36、Build-Tools 36.1.0、NDK 28.2.13676358、CMake 3.22.1。

```powershell
dotnet run --project windows-native/FClash.Tests -c Release
./scripts/build_windows_native.ps1 -Architecture x64 -Label 0.8.97 -Targets zip
python scripts/build_android.py
```

Android 签名继续从本地忽略的 `android/key.properties` 读取；签名密钥、密码禁止提交。安全模式构建：`python scripts/build_android.py --debug --safe-mode --abis arm64-v8a`。Gradle 可独立使用，例如 `android/gradlew.bat -p android :domain:test`。

版本来自 `version.properties`，标签末段保留两位，满 100 进位：`0.8.99 → 0.9.01`。`semver` 使用合法数值版本，例如 `0.9.1`；Android `code` 必须单调递增。

## 升级与验证

Android 原生版从同一包名的旧版只读导入 SQLite、配置、脚本、选择、DNS/规则顺序和国旗缓存，原数据库保留；新数据存入应用内部 `files/native`。支持原生 ZIP 备份与旧版 ZIP 数据导入。Windows 使用独立原生目录，详见 [Windows 说明](windows-native/README.md)。

开发机仅运行隔离安全模式，禁止更改开发机代理、DNS、TUN 或 helper 服务。Android 安全模式使用 `.dev` 包名和独立数据目录，禁止启动 Core/VPN。正式 VPN、权限授权、系统回收后的恢复和内存收益必须在专用测试设备上验证。不得把框架迁移等同于已测得的内存下降。
