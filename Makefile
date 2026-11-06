# Native clients only. Initialize core/Clash.Meta before building.
.PHONY: android android-safe windows test
android:
	python3 scripts/build_android.py
android-safe:
	python3 scripts/build_android.py --debug --safe-mode --abis arm64-v8a
windows:
	pwsh -NoProfile -File scripts/build_windows_native.ps1 -Targets zip
test:
	dotnet run --project windows-native/FClash.Tests -c Release
	cd android && ./gradlew :domain:test
