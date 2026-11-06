# FClash Android native

Kotlin / Compose Material 3 UI, independent `:domain` configuration module, existing `:service`, `:core` and `:common` platform modules. `:remote` owns the VPN and Go Core; the UI connects through Binder/AIDL, never a Flutter engine or MethodChannel.

## Build

```powershell
python scripts/build_android.py
python scripts/build_android.py --debug --safe-mode --abis arm64-v8a
```

The script builds Go C-shared libraries and JNI headers for arm64-v8a, armeabi-v7a and x86_64, then runs domain tests, Gradle packaging and lint. `--skip-core` is only for existing complete Core/JNI outputs. Set `JAVA_HOME`, `ANDROID_HOME` and optionally `ANDROID_NDK`. Signing uses the existing ignored `android/key.properties`; the application ID remains `com.follow.fclash`. Never publish the `.dev` debug package as a release.

## Data and lifecycle

The first normal launch imports legacy SQLite/profile/script/preferences data read-only into `files/native/native-state.json`, leaving the original files available. Old preference names remain only for upgrade compatibility. DNS and additional-rule list order is preserved through YAML generation. Manual delay tests alone update per-profile country flags; failed country lookups retain cached flags. Activity lifecycle controls statistics/event subscriptions. The VPN foreground service remains independent of the Activity.

Backups contain state, profiles and scripts, excluding Geo data and runtime sockets. Restore uses bounded staging, validates paths/configuration, and creates a recovery ZIP before replacing state. JavaScript overrides use a bounded Rhino interpreter with Java class access disabled.

Safe-mode builds (`-Pfclash.safeMode=true`) have an isolated directory and prohibit Core, VPN and network probes. They must not be installed over a user's existing development app for automated testing. Dedicated-device validation is required for VPN permissions, OEM background policies, SSID/location access, quick tiles, upgrade/restore and real proxy traffic.

For R8 verification, build `:app:assembleDebug` with `-Pfclash.safeMode=true -Pfclash.minifyDebug=true`, then run `adb -s <dedicated-emulator> shell am instrument -w com.follow.fclash.dev/com.follow.fclash.NativeSmokeInstrumentation`. This runner is debug-only and checks scripts, Java isolation, execution limits, YAML and persistence inside the app APK. The separate Compose/SQLite instrumentation suite uses the ordinary unminified safe debug build: `:app:assembleDebug :app:assembleDebugAndroidTest -Pfclash.safeMode=true`, then AndroidJUnitRunner. A separate test APK can otherwise shadow the minified app's desugared Java library with an incompatible subset. Release builds have no smoke instrumentation entry. Build-time Crashlytics map/symbol upload is disabled.
