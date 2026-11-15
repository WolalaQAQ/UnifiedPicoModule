# Host compatibility checks

The same module DLL, compiled against VRCFT 5.4.5, is loaded in separate processes using the pinned official 5.4.5 and Steam 6 Core/SDK assemblies. Each process uses private AppData and initializes tracking with both network sources disabled. Tests inject immutable input snapshots, exercise the production decoder, processing and SDK write boundary, then inspect official IPC and OSC output.

The Steam 6 tests cover the official module loader, 102-byte native face ordering and quantization, legacy Avatar routing, separate channel ownership, measured versus unavailable pupils and expiry. A loopback receiver uses an OS-assigned port. No GUI, headset, production tracking port or existing user profile is used.

Run from the repository root after building the module:

```powershell
pwsh -NoProfile -File tools/test-compatibility.ps1
```

The default source paths are `.sdk/source/VRCFaceTracking` at `7add14873e398c4095d06913cdeee7a01fc43fab` and `.sdk/compat/source/vrcft6-steam` at `3d36379253d26961560d84acecd01d509aea467a`. `-LegacySdkSource` and `-NativeSdkSource` accept other checkout locations. Use `-ModuleDll` to test an existing DLL instead of building first. `-InstalledHostDir` optionally uses matching Steam-delivered Core, SDK, ModuleProcess and native OSC DLLs in the isolated test output; it never changes the installation.

Results are written under `artifacts/compat/<timestamp>/`. `KNOWN-UPSTREAM` cases reproduce the stock host's overlapping left-outer-brow ownership and native sender start/stop subscription behavior; passing those cases does not mean those host defects are fixed. Mixed-owner tests use synthetic sources, not the real Cymple module or Avatar rendering.

The independent tracking-key fixture comes from the [VRChat OSC face tracking specification](https://creators.vrchat.com/avatars/face-tracking/osc), checked on 2026-11-05.
