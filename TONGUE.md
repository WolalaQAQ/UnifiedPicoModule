# PC end-to-end mouth receiver — development branch

English | [简体中文](TONGUE_zh.md)

Use `feat/tongue-pc` in both this repository and [PicoET-Enhance](https://github.com/WolalaQAQ/PicoET-Enhance/tree/feat/tongue-pc). The [sender guide](https://github.com/WolalaQAQ/PicoET-Enhance/blob/feat/tongue-pc/TONGUE.md) covers camera capture, the PC app, per-channel native/model routing (`Hybrid` vs `ModelOnly`), regression models and local teacher/student distillation. The headset captures images, the PC app runs inference and channel routing, and this module writes the received mouth vector directly to the VRCFT SDK when an `MFR1` stream is active while automatically unsubscribing Bridge face channels without interrupting eye tracking.

## Build and use

1. Prepare the pinned official VRCFT SDK and build using [Development](docs/development.md). The dependency remains VRCFT 5.4.5 / .NET 10.
2. With VRCFT closed, install your built module following [Installation](README.md#installation). Keep your existing configuration; do not load two copies of this module.
3. Let VRCFT assign face output to the PICO module when testing whole-mouth regression. When PC Studio's `MFR1` stream covers all 33 native mouth channels, this module yields Bridge face demand (`PXR_SUB` mask 3 → 1) while keeping eyes running. If `MFR1` covers only a subset (such as a tongue-only personal model or while headset `PSL1` is unavailable), Bridge keeps face tracking active (`mask=3`) so native jaw and lip channels continue while `MFR1` overlays its valid bits. Stopping or expiring clears extended fields and returns cleanly to direct Bridge tracking.
4. Enable the app's local UDP output to `127.0.0.1:45682`, then start live input. `Tongue.Port` must match the app's output port.

The configuration section defaults to:

```json
"Tongue": { "Enabled": true, "Port": 45682 }
```

`Port` accepts 1–65535; invalid values fall back to 45682. Omitted settings use defaults without rewriting an existing file. `Enabled=false` disables the receiver. The bind address is always IPv4 loopback. Bind failure is logged and leaves normal PICO tracking running. Configuration changes take effect on module initialization. If an older JSON still contains `Tongue.MouthMode` or `Tongue.HybridChannels`, they are logged as unknown keys and ignored; configure channel routing in PC Studio instead.

Only `ownsFace && Tongue.Enabled` opens the socket. With Cymple mouth + PICO eyes, the PICO module opens no tongue port and writes no foreign mouth/tongue fields. Keep that ownership split in the host.

## MFR1 whole-mouth output

End-to-end models use 256-byte MFR1 with the `pico-mouth50-v1` schema on the same loopback UDP port. It carries 50 named continuous controls and a validity mask. The original 44 fields retain their order; four independent upper/lower lip horizontal controls and two upper/lower Funnel controls are appended. The manifest's explicit `valid_mask` may enable only independently `validated_channels` within `trained_channels`; a zero-mask candidate stays disabled.

Channel selection between headset native `PSL1` (`final33`) and PC model predictions happens live in PC Studio (`mouth_mode`: `curated`, `hybrid`, `model_only`, or `custom`). Explicitly disabled (`off`) channels are sent as `valid_mask=1, value=0.0`, while unsupported channels or unavailable `PSL1` stock channels keep `valid_mask=0`. Whenever fresh `MFR1` frames arrive here, this module writes only the channels set in `valid_mask` and yields `PicoFacialBridge` face forwarding (`PXR_SUB mask=1` when eyes are active, without resetting live eye state) only when all 33 native mouth channels are covered (including aggregate/split equivalence). Partial-channel models or `PSL1` dropouts keep Bridge face tracking active (`mask=3`) so native jaw and lip channels are preserved. When extended bits drop out or `MFR1` releases, disconnects or expires, previously written extended fields (`33..49`) are cleared. Eyes always retain their own direct Bridge path; when another module such as Cymple owns the mouth, this receiver neither opens nor writes/clears foreign mouth fields.

MFR1 does not apply legacy TJR tongue-presence thresholds, direction remapping or an `Out * direction` gate. Zero and small nonzero predictions remain model controls. Sequence, sensor time, session retirement and source age enforce freshness; duplicates do not renew expiry. See [Protocol](docs/protocol.md) for the exact layout.

The configuration section retains its `Tongue` name so existing configurations keep using port 45682; new results are not tongue-only. There is no ORT or teacher dependency in this receiver and no modification to the official VRCFT host.

## Legacy TJR1/TJR2 output

| PC result | Module output |
|---|---|
| Fresh present | Replace Out / Left / Right; TJR2 also supplies Up / Down / Roll |
| Fresh absent | Set the model's tongue channels to zero, even if native Out is positive |
| Uncertain | Hold a prior positive for at most 100 ms, bounded by its original deadline |
| Invalid, stop, expired or missing | Restore this frame's native pipeline Out and clear Left / Right |

There is no `max(native, model)` merge. TJR1-only receivers leave Up / Down / Roll untouched. Once TJR2 has supplied them, absent, invalid, expiry, teardown and switching back to TJR1 clear those extensions to avoid latched poses. TJR2 directions are zero when Out is zero. `LegacyExpressionCeiling` applies to all PC channels. Main-source hold-last settings do not disable mandatory tongue expiry. UDP threads update state only; the existing Write boundary remains the sole SDK writer. Pause, Update failure and teardown cancel and join the worker before releasing its socket.

## TJR1 wire format

Each datagram is exactly 64 bytes, little-endian, with no discovery, subscription or reply. This is not the old headset UDP19031 / TNG2 protocol and is not an `InputProtocol` main source.

| Offset | Type | Field |
|---:|---|---|
| 0 | 4 bytes | ASCII `TJR1` |
| 4 / 6 | uint16 each | Version 1 / length 64 |
| 8 / 16 | uint64 each | Nonzero PC output session / source sequence |
| 24 / 32 | uint64 each | Original mouth sensor ns / emitted PC monotonic ns |
| 40 / 44 / 48 | float32 each | Left / Right / Out, finite, [0,1] |
| 52 | float32 | Source age at emission in ms, finite, nonnegative |
| 56 / 60 | uint32 each | State / reserved zero |

State is exactly present=1, absent=2, uncertain=4, invalid=8 or stop=16. Normal results require increasing sequence and mouth sensor time. Invalid/stop may repeat the current sequence to release output but cannot use an older sequence; stop closes the session. New source/model/profile epochs use a new output session. Retired sessions cannot return.

The maximum 350 ms lifetime is reduced by reported source age. A source-clock anchor also prevents accumulated transmission backlog from renewing old images. Duplicates, old sequences and repeated sensor times never extend the deadline.

### TJR2 extension

MouthLink models in the PC app use 80-byte TJR2 on the same loopback port. Offsets 0/4/6 contain `TJR2` / version 2 / length 80. All other TJR1 fields retain their offsets, followed by float32 Up at 64, Down at 68, Roll at 72 and uint32 reserved zero at 76. Extension values must be finite and in [0,1]. State, source ordering, expiry and session retirement follow the same rules as TJR1. Use the updated receiver for TJR2; old receivers reject it rather than misread its values.

MouthLink inference stays in the PC app using its existing ORT backends. The module has no new native runtime dependency and does not take over the remaining mouth shapes. This is numeric interoperability, not a bundled or validated personal model.

## Offline checks

After `tools/build.ps1` prepares `.sdk/`:

```powershell
./tools/test-tongue.ps1 -ArtifactsPath artifacts/tongue-check
python tools/test-tongue-wire.py --dll artifacts/tongue-check/bin/TongueTests/release/TongueTests.dll --pc-wire ../PicoET-Enhance/pc-studio/wire.py --output artifacts/tongue-wire
```

The first command tests the production parser/state machine, real SDK Write ownership, loopback UDP and lifecycle. The second uses the matching app's actual Python encoder and the isolated C# probe, including a foreign-face ownership case. Both use ephemeral loopback ports, not a running VRCFT installation. Use a new output directory for each wire check.

These checks do not establish worn-person accuracy or Avatar behavior. Test live tracking, calibration, retraining and native eye/mouth coexistence using the paired sender branch.

## Upstream basis

This replaces the receiver proposed by [PR #1](https://github.com/WolalaQAQ/UnifiedPicoModule/pull/1), reviewed at `f91d18d46e5661ea9e205eee9d854d965c4c99bc`, with a PC-local TJR1/TJR2 receiver. The additional tongue directions draw on UndefinedSignal's [MouthLink contribution](https://github.com/WolalaQAQ/PicoET-Enhance/issues/5). The old `Sdk/TongueReceiver.cs`, headset subscription, in-module ORT and native/model maximum merge are not included. No unrelated compatibility/CI changes are part of this branch.
