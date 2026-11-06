# Native commands
Initialize `core/Clash.Meta` first.
```powershell
dotnet run --project windows-native/FClash.Tests -c Release
dotnet build windows-native/FClash.WinUI -c Release -p:Platform=x64
./scripts/build_windows_native.ps1 -Architecture x64 -Label 0.8.97 -Targets 'exe,zip'
python scripts/build_android.py
python scripts/build_android.py --debug --safe-mode --abis arm64-v8a
android/gradlew.bat -p android :domain:test
```
Android signing uses ignored local configuration; preserve the existing FClash release key. `--skip-core` requires complete library/header outputs for each selected ABI. Never commit local network/SDK paths.
Host verification: `FClash.exe --safe-mode --data-dir C:/absolute/workspace/build/native-safe-data`. Android checks use a fresh dedicated emulator/test package. Never change the user's proxy/DNS/TUN/helper state or install over an existing user app. Memory sampling is read-only against recorded test PIDs; safe-mode UI results do not establish production improvements.
