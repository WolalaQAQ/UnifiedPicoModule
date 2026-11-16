# Changelog

English | [简体中文](CHANGELOG_zh.md)

## [0.4.1-tongue.4] - 2026-11-10 (fork Rulez777/UnifiedPicoModule)

### Features
- Tongue left/right from the PicoET-Enhance tongue payload over Wi-Fi (UDP 19031), rebased onto 0.4.1: works with VRCFT 5.4.5 and Steam VRCFT 6.0.0.0.
- `Install.cmd` in the release zip copies the module into VRCFaceTracking's `CustomLibs` and keeps the existing configuration.
- No tongue output while PICO reports a cheek puff (above 0.3, full at 0.5, held 400 ms): puffed cheeks made a false tongue appear.

### Changed
- Tongue diagnostics are off by default: `tongue-status.json` and the once-a-second log line appear only when `tongue-diagnostics.enabled` exists next to the DLL.

## [0.4.1] - 2026-11-05

### Features
- Support Steam VRCFT 6.0.0.0 (`3d363792`) with the same DLL used by VRCFT 5.4.5.
- Fix missing pupil measurements being encoded as constriction in VRCFT 6; retain measured values and VRCFT 5's configured neutral diameter.
- Add isolated compatibility checks for both official host versions, including native OSC output and legacy Avatar routing.

### Design Rationale
- Use VRCFT 6's unavailable-measurement sentinel only at the SDK write boundary, keeping source selection, processing and configuration defaults unchanged.
- Keep the 5.4.5 compilation target and use the host's existing OSC sender; no replacement host or SDK is included.

### Notes & Caveats
- Official 5.4.5 and Steam 6 component checks cover loading, channel ownership, input clocks, pupil validity/expiry and actual OSC serialization. Real Cymple/VRCFT 6 mixing and Avatar rendering remain untested.

## [0.4.0] - 2026-10-17

### Features
- Accept BridgeSplit `X` v2 with separate left-eye, right-eye and mouth source clocks, update bits, reception sequences and freshness.
- Report per-group reception, processing and coalescing, plus new versus repeated SDK writes and timing distributions.
- Keep the module process's timer resolution effective when VRCFT runs in the background. In testing this restored the module's consumption rate to about 90 Hz.

### Design Rationale
- Process only groups with a new source sample. Fast eye updates retain mouth output without rerunning mouth smoothing, tongue holds or channel gain.
- Save the module process's Windows timer policy and clear only `IGNORE_TIMER_RESOLUTION` while tracking. On stop or failure, restore only that bit's original control/state settings, preserving other current policy bits. This keeps official VRCFT 5.4.5's timer request effective in the background without changing priorities or CPU throttling. It applies to the module's own sandbox process only; the VRCFT host process is not touched.
- Build against the official VRCFaceTracking release commit `7add148` (the Steam build, assembly 5.4.5.0) instead of a later upstream commit. The module code and its output are identical under both.

### Notes & Caveats
- `X` v1 and existing formats remain supported; `X` v2 requires PicoFacialBridge 0.5.0 or later and PicoET-Enhance 2.3.0 or later. Configuration defaults, expression-derived gaze/eyelids and measured-pupil handling are unchanged.
- Validated on a PICO 4 Pro with official VRCFT 5.4.5 in an 84-second worn Virtual Desktop session (Cymple mouth with PICO eyes), plus eye-only and combined PICO tracking. About 89.7 `X` packets per second arrived with a new eye source each. This was a short session, not a long-duration stability test.
- SDK write counts are not OSC or Avatar refresh rates. The official host passes module output to OSC at roughly 61–78 Hz; the patch described in the PicoET-Enhance README raises that to about 87–88 Hz.

## [0.3.0] - 2026-10-15

### Features
- Decode BridgeSplit `X` v1 packets carrying independent right inner brow and per-eye quality states alongside the standard eye/face shapes.
- Derive gaze from the `EyeLook` shapes and eyelid openness from `1 - EyeBlink` for every sender and packet type, including independent-expression `X` packets.

### Changed
- The 83-byte `E` packet keeps its wire format and now supplies only the measured pupil diameter; its native gaze and openness fields are decoded but not output.
- Existing configurations keep loading after the removed fields below are dropped: they are reported as unknown keys and ignored, and the file is not rewritten.

### Removed
- Remove the native gaze/openness resolution branch and the `AdaptiveGazeFilter` smoothing filter.
- Remove the `EyePolicy`, `OpennessPolicy`, `GazeSmoothing` and `LegacyPerEyeGaze` configuration fields.

### Design Rationale
- Track per-eye cache provenance so an initially invalid X eye cannot inherit prior fused/native or ordinary-expression animation values; later invalid samples hold that eye's own last valid value.
- One expression-driven eye path keeps ordinary and independent packets identical in behavior, and removes a smoothing stage whose parameters were selected only offline.
- Preserve official VRCFT compatibility and separate brow values. Combined-brow averaging remains a host/avatar parameter choice; no host replacement or avatar-specific maximum-copy mode is included.

### Notes & Caveats
- Requires sender support for `X`: PicoFacialBridge 0.4.0 or later, with PicoET-Enhance 2.2.0 or later for independent per-eye expressions. Normal mode still accepts Bridge 0.3.0 or later; older receivers reject `X`. Quality state -1 remains unknown while its supplied data is usable.
- Validated on official VRCFT 5.4.5 with PicoFacialBridge 0.4.0 and PicoET-Enhance 2.2.0: recorded eye images replayed through native eye inference reached the PC, and `E` and dual-channel `X` values matched their source outputs. The replay used about 23.7 Hz recorded images in an accelerated loop, so it verifies inference throughput and delivery, not an original 90 Hz camera sequence.
- Release builds of the module and ConfigTool succeed. Single-eye quality failure (state 0), reference recovery and long live-camera use remain unverified. The Avatar's combined brow parameter averages the two sides; separate left/right brow parameters are needed to see independent values.

## [0.2.0] - 2026-10-11

### Added
- Enable per-eye OneEuro smoothing for native gaze by default, adapting strength to eyelid closure.
- Add `DualLightCutoffHz` and `DualBeta` for independent enhanced-mode light filtering and speed response. Logs show effective parameters and per-eye filter weights.

### Changed
- Default normal gaze to 100/4 Hz, Beta 1 and Blink 0.30/0.60; dual to 250/4 Hz, Beta 8 and Openness 0.80/0.45. Confirmation and eyelid smoothing are 350/150 ms.
- Fresh configurations and omitted fields use the defaults; explicit values are retained. Legacy-module imports continue to disable the added smoothing.

### Fixed
- Fix eyelid jumps caused by temporary Blink fallback when enhanced native openness is invalid. Hold that eye's last valid value instead.
- Select gaze sources by protocol and capability, retaining the last value during invalidity; align automatic source eligibility with this selection.
- Hold the last output across data and connection timeouts by default. Set the new `Stale.NeutralizeOnTimeout` option to `true` to restore timeout neutralization.
- Disable filtering when smoothing configuration validation fails.

## [0.1.2] - 2026-10-10

### Fixed
- Use EyeBlink for eyelid closure in normal mode, fixing partial closure being overridden by native Openness fixed at 1.

### Added
- Add `OpennessPolicy`, defaulting to `Auto`: normal mode uses Blink, while enhanced mode prefers native Openness with Blink fallback. Select `BlendshapeOnly` or `NativePreferred` for a fixed eyelid source policy; gaze remains configured by `EyePolicy`.
- Log the openness source and support independent eyelid policy in automatic source selection and configuration migration.

### Validation
- Module and ConfigTool Release builds pass with no warnings or errors; the default and example configurations pass validation.
- Headset revalidation is pending.

## [0.1.1] - 2026-10-03

### Fixed
- Capture immutable source snapshots before sampling the evaluation clock. A concurrently received frame can no longer appear to arrive in the future, briefly clear facial output, reset temporal postprocessing, or trigger an unnecessary source fallback.
- Use the same snapshots and timestamp for source selection, tracking evaluation and diagnostics.

### Validation
- Module and ConfigTool Release builds pass with no warnings or errors.
- The concurrency fix has not yet been revalidated on a headset or under network scheduling stress.

## [0.1.0] - 2026-10-01

### Added
- Initial independent VRCFaceTracking module targeting VRCFT 5.4.5 / .NET 10.
- BridgeSplit subscriptions, legacy daemon packets, and streaming formats on ports 29765 and 29763, with automatic source selection and optional fallback.
- Native-priority eye tracking, expression mapping, configurable postprocessing, and an offline configuration and migration tool.
