"""Build Go/JNI and the Kotlin Android app. Requires no Dart or Flutter SDK."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
TARGETS = {"arm64-v8a": ("arm64", "aarch64-linux-android"), "armeabi-v7a": ("arm", "armv7a-linux-androideabi"), "x86_64": ("amd64", "x86_64-linux-android")}

def run(command, cwd=ROOT, env=None):
    subprocess.run([str(part) for part in command], cwd=cwd, env=env, check=True)

def build_core(abis):
    sdk = Path(os.environ.get("ANDROID_HOME") or os.environ.get("ANDROID_SDK_ROOT") or Path.home() / "AppData/Local/Android/Sdk")
    ndk = Path(os.environ.get("ANDROID_NDK") or sdk / "ndk/28.2.13676358")
    host = "windows-x86_64" if os.name == "nt" else "linux-x86_64"
    compiler = ndk / "toolchains/llvm/prebuilt" / host / "bin/clang"
    if os.name == "nt": compiler = compiler.with_suffix(".exe")
    if not compiler.is_file(): raise SystemExit("Set ANDROID_NDK to an installed NDK 28.2.13676358 directory")
    for abi in abis:
        print("Building Core:", abi, flush=True)
        arch, triple = TARGETS[abi]
        output = ROOT / "android/core/src/main/jniLibs" / abi / "libclash.so"
        output.parent.mkdir(parents=True, exist_ok=True)
        environment = dict(os.environ, GOOS="android", GOARCH=arch, CGO_ENABLED="1", CC=f'"{compiler}" --target={triple}23', CGO_CFLAGS="-O3", CGO_LDFLAGS="-Wl,-z,max-page-size=16384")
        if arch == "arm": environment["GOARM"] = "7"
        run(["go", "build", "-trimpath", "-tags", "with_gvisor", "-ldflags=-s -w", "-buildmode=c-shared", "-o", output, "."], ROOT / "core", environment)
        include = ROOT / "android/core/src/main/cpp/includes" / abi
        include.mkdir(parents=True, exist_ok=True)
        shutil.copy2(output.with_suffix(".h"), include / "libclash.h")
        shutil.copy2(ROOT / "core/bride.h", include / "bride.h")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--abis", default=",".join(TARGETS))
    parser.add_argument("--debug", action="store_true")
    parser.add_argument("--safe-mode", action="store_true")
    parser.add_argument("--skip-core", action="store_true")
    parser.add_argument("--gradle-arg", action="append", default=[])
    args = parser.parse_args()
    abis = args.abis.split(",")
    if any(abi not in TARGETS for abi in abis): parser.error("Unsupported ABI")
    if args.safe_mode and not args.debug: parser.error("Safe-mode artifacts must use the debug application ID")
    if not args.debug:
        signing = ROOT / "android/key.properties"
        if not signing.is_file() and not (ROOT / "android/app/keystore.jks").is_file():
            raise SystemExit("Release signing is required; configure android/key.properties (never committed)")
    if not args.skip_core: build_core(abis)
    for abi in abis:
        for path in [ROOT / f"android/core/src/main/jniLibs/{abi}/libclash.so", ROOT / f"android/core/src/main/cpp/includes/{abi}/libclash.h", ROOT / f"android/core/src/main/cpp/includes/{abi}/bride.h"]:
            if not path.is_file(): raise SystemExit(f"Missing Core build output: {path}")
    wrapper = ROOT / "android" / ("gradlew.bat" if os.name == "nt" else "gradlew")
    command = [wrapper, ":domain:test", ":app:assembleDebug" if args.debug else ":app:assembleRelease", ":app:lintDebug" if args.debug else ":app:lintRelease", f"-Pfclash.abis={args.abis}", f"-Pfclash.safeMode={str(args.safe_mode).lower()}", "--no-daemon", *args.gradle_arg]
    if not args.debug: command.append("-Pfclash.requireReleaseSigning=true")
    run(command, ROOT / "android")
    mode = "debug" if args.debug else "release"
    source = ROOT / f"build/app/outputs/apk/{mode}/app-{mode}.apk"
    version = dict(line.split("=", 1) for line in (ROOT / "version.properties").read_text().splitlines() if "=" in line)["label"]
    suffix = "-safe" if args.safe_mode else ""
    destination = ROOT / f"dist/android-native/FClash-{version}-native{suffix}-android-{mode}.apk"
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists(): raise SystemExit(f"Artifact exists; preserve it or choose a new release label: {destination}")
    shutil.copy2(source, destination)
    destination.with_suffix(".apk.sha256").write_text(hashlib.sha256(destination.read_bytes()).hexdigest() + "  " + destination.name + "\n")
    print(destination)

if __name__ == "__main__": main()
