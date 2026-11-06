---
name: core-platform
description: Use when changing FClash native Windows or Android Core integration, transport, VPN, lifecycle or builds.
---
# Core/platform
Read `.agents/architecture.md`. Identify the boundary: Windows ClientSession/CoreClient, Android NativeSession/Service/AIDL/JNI, or Go Core. Route page logic through session facades. Preserve protocol compatibility, bounded payloads and cancellation. Test ordered YAML, configuration rollback and owned process exit. Build affected platforms. Host checks use isolated safe mode and never change the user's network. Do not restore detached Windows Core sessions.
