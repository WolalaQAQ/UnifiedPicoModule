# Host Compatibility Test Harness

**English** | [简体中文](README_zh.md) · [Development Guide](../../docs/development.md) · [README](../../README.md)

This harness verifies that a **single** `UnifiedPicoModule.dll` (compiled against VRCFaceTracking 5.4.5 SDK) loads cleanly and behaves consistently on both official VRCFT 5.4.5 and Steam VRCFT 6 (`6.0.0.0`).

## How It Works

`tools/test-compatibility.ps1` runs two isolated processes—one linked against pinned VRCFT 5.4.5 (`vrcft5`) and one against pinned Steam VRCFT 6 (`vrcft6`). Each process:

- Uses a private `APPDATA` and `LOCALAPPDATA` profile under `artifacts/compat/<timestamp>/<host>/profile/`.
- Initializes the module with both network sources disabled and injects immutable `SourceSnapshot` or synthetic wire-byte fixtures directly into the production decoder, pipeline, and SDK write boundary.
- Uses an OS-assigned loopback UDP port (`127.0.0.1:0`) when verifying Steam 6 OSC bundles.
- Never launches the GUI, never binds production tracking ports (`9030`, `29765`, `29763`), and never reads or modifies your VRCFT installation or user profile.

## Test Coverage

### Shared Suite (VRCFT 5.4.5 & Steam VRCFT 6)

- **Lifecycle & channel ownership (`Program.cs`):** Default config generation when missing, idempotent `Initialize`/`Teardown`, embedded PNG icon readability, per-channel ownership isolation (`ownsEye` / `ownsFace`), pause/error neutralization without clobbering foreign channels, and `SourceCoordinator` sticky selection + `AllowFallback`.
- **Pupil provenance (`Program.cs`):** Real measured pupil millimeters pass through on both hosts; unavailable pupils emit `0` on VRCFT 6 (`NATIVE_HOST`) and `Stale.NeutralPupilMillimeters` on VRCFT 5.
- **Wire decoding & grouped clocks (`InputTests.cs`):** `BridgeSplitDecoder` validation for `F`, `X` v1, and `X` v2 (`263`-byte full packets, malformed rejections), independent left/right/mouth timestamp deduplication, `groupUpdated` bitmask no-op & clock preservation, `EyeGain`/`ChannelGain` non-compounding, `TongueHold` expiry without mouth reprocessing, per-eye quality failure (`state == 0`) & stale neutralization, and timeout vs. explicit session resets.
- **Sandbox IPC & legacy OSC (`Program.cs`, `LegacyTests.cs`):** `ReplyUpdatePacket` eye/mouth roundtrip, legacy `/avatar/parameters/v2/*` formulas (`JawOpen`, averaged `BrowInnerUp`), and big-endian OSC float encoding.

### Steam VRCFT 6 Native-Host Suite (`NATIVE_HOST` Only)

- **Official module loader (`ModuleLoaderTests.cs`):** Loads a copied `UnifiedPicoModule.dll` via `VRCFaceTracking.ModuleProcess.ModuleAssembly` inside a collectible `AssemblyLoadContext`, confirming the module's 5.4.5 `VRCFaceTracking.Core` / `VRCFaceTracking.SDK` references bind to the shared host 6.0 assemblies without private duplicate loads, plus invalid path/extension rejection.
- **102-byte native face blob (`NativeTests.cs`):** Verifies the `/tracking/face/v1` 132-byte OSC message (`#bundle`, `,b`, 102-byte big-endian blob, padding) against `face-v1-keys.txt` (from the [VRChat OSC face tracking specification](https://creators.vrchat.com/avatars/face-tracking/osc), checked 2026-11-05), clamping/quantization, all 10 eye directions/blinks, PC post-processing + independent brows, and disposed `OscMessage` resilience.
- **Avatar capability routing & pupils (`NativeTests.cs`):** OSCQuery `avatarfullfacetracking` switching between native `/tracking/face/v1` and legacy `/avatar/parameters/v2/*`, measured vs. unavailable pupil serialization vectors (`0` → neutral; `2.5`/`7.5` mm → constriction/dilation), and hold vs. timeout neutralization on expiry/teardown.
- **Mixed eye/mouth ownership (`NativeTests.cs`):** Exercises separate eye-only and mouth-only instances over sandbox IPC using synthetic sources (not the real Cymple module or Avatar rendering), including sentinel preservation and mouth teardown leaving eye output intact.

## Prerequisites

Check out both pinned VRCFaceTracking commits from the repository root:

```powershell
# Official VRCFT 5.4.5 (also used by tools/build.ps1)
git clone https://github.com/benaclejames/VRCFaceTracking.git .sdk/source/VRCFaceTracking
git -C .sdk/source/VRCFaceTracking switch --detach 7add14873e398c4095d06913cdeee7a01fc43fab

# Steam VRCFT 6.0.0.0 (Build 26384174)
git clone https://github.com/benaclejames/VRCFaceTracking.git .sdk/compat/source/vrcft6-steam
git -C .sdk/compat/source/vrcft6-steam switch --detach 3d36379253d26961560d84acecd01d509aea467a
```

## Running the Tests

From the repository root:

```powershell
# Build the module (Release) and run both host suites
pwsh -NoProfile -File tools/test-compatibility.ps1
```

### Optional Parameters

| Parameter | Default | Description |
|---|---|---|
| `-ModuleDll` | *(builds Release first)* | Test an existing `UnifiedPicoModule.dll` without rebuilding |
| `-LegacySdkSource` | `.sdk/source/VRCFaceTracking` | Path to pinned 5.4.5 checkout (`7add14873e398c4095d06913cdeee7a01fc43fab`) |
| `-NativeSdkSource` | `.sdk/compat/source/vrcft6-steam` | Path to pinned Steam 6 checkout (`3d36379253d26961560d84acecd01d509aea467a`) |
| `-InstalledHostDir` | *(none)* | Optional path to a Steam VRCFT 6 install matching commit `3d363792...`; copies `VRCFaceTracking.{Core,SDK,ModuleProcess}.dll` and `fti_osc.dll` into the isolated test output without modifying the installation |

Example with a prebuilt DLL:

```powershell
pwsh -NoProfile -File tools/test-compatibility.ps1 -ModuleDll bin/Release/net10.0/UnifiedPicoModule.dll
```

## Output & `KNOWN-UPSTREAM` Cases

Each run writes structured evidence under `artifacts/compat/<yyyyMMdd-HHmmss-fff>/`:

- `summary.json` — module path, optional installed host path, and links to per-host reports
- `vrcft5/results.json`, `vrcft6/results.json` — pass/fail list, host assembly versions, and `knownUpstream` notes
- `vrcft5/tests.log`, `vrcft6/tests.log` (plus `build.log`) — full test runner output

Two `KNOWN-UPSTREAM` probes document stock host behavior rather than module defects:

1. **Overlapping `BrowOuterUpLeft` IPC range:** Both pinned 5.4.5 and Steam 6 include `BrowOuterUpLeft` in both eye and mouth IPC copy ranges, so a non-sentinel mouth IPC reply can overwrite it depending on reply order.
2. **`BinaryFaceDataSender` subscription lifecycle:** On Steam 6, the stock sender starts subscribed before capability events arrive, and subsequent capability events can resubscribe a stopped instance.

Passing these probes confirms the module stays compatible with stock host behavior without patching `VRCFaceTracking.Core`; it does not indicate those upstream behaviors have changed.
