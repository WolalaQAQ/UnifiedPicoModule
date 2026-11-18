# PC end-to-end mouth receiver — development branch

English | [简体中文](TONGUE_zh.md)

Use `feat/tongue-pc` in both this repository and [PicoET-Enhance](https://github.com/WolalaQAQ/PicoET-Enhance/tree/feat/tongue-pc). The [sender guide](https://github.com/WolalaQAQ/PicoET-Enhance/blob/feat/tongue-pc/TONGUE.md) covers camera capture, the PC app, recording, regression models and local teacher/student distillation. The headset captures images, the PC handles configuration and inference, and this module merges numeric results at the existing VRCFT Write boundary. It does not run models or start the camera.

## Build and use

1. Prepare the pinned official VRCFT SDK and build using [Development](docs/development.md). The dependency remains VRCFT 5.4.5 / .NET 10.
2. With VRCFT closed, install your built module following [Installation](README.md#installation). Keep your existing configuration; do not load two copies of this module.
3. Let VRCFT assign face output to the PICO module when testing whole-mouth regression. The PC app does not select or override the mouth source.
4. Enable the app's local UDP output to `127.0.0.1:45682`, then start live input. `Tongue.Port` must match the app's output port.

The configuration section defaults to:

```json
"Tongue": { "Enabled": true, "Port": 45682 }
```

`Port` accepts 1–65535; invalid values fall back to 45682. Omitted settings use defaults without rewriting an existing file. `Enabled=false` disables the receiver. The bind address is always IPv4 loopback. Bind failure is logged and leaves normal PICO tracking running. Configuration changes take effect on module initialization.

Only `ownsFace && Tongue.Enabled` opens the socket. With Cymple mouth + PICO eyes, the PICO module opens no tongue port and writes no foreign mouth/tongue fields. Keep that ownership split in the host.

## MFR1 whole-mouth output

End-to-end models use 256-byte MFR1 with the `pico-mouth50-v1` schema on the same loopback UDP port. It carries 50 named continuous controls and a validity mask. The original 44 fields retain their order; four independent upper/lower lip horizontal controls and two upper/lower Funnel controls are appended. Validity comes from the model manifest's trained channels, not simply from having 50 numeric outputs.

| PC result | Module output |
|---|---|
| Fresh, supported field | Override the corresponding PICO mouth output; independent lip/cheek controls take precedence over legacy aggregate fan-out |
| Unsupported/unknown field | A zero payload is not a neutral measurement; existing stock channels continue to use the current PICO value |
| Invalid, stopped or expired | Release model overrides, restore current PICO values and release new extensions previously written by this model |
| Face owned by another module | Neither write nor clear that module's mouth/tongue fields |

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
python tools/test-tongue-wire.py --dll artifacts/tongue-check/bin/TongueTests/release/TongueTests.dll --pc-wire ../PicoET-Enhance/pc-tongue/wire.py --output artifacts/tongue-wire
```

The first command tests the production parser/state machine, real SDK Write ownership, loopback UDP and lifecycle. The second uses the matching app's actual Python encoder and the isolated C# probe, including a foreign-face ownership case. Both use ephemeral loopback ports, not a running VRCFT installation. Use a new output directory for each wire check.

These checks do not establish worn-person accuracy or Avatar behavior. Test live tracking, calibration, retraining and native eye/mouth coexistence using the paired sender branch.

## Upstream basis

This replaces the receiver proposed by [PR #1](https://github.com/WolalaQAQ/UnifiedPicoModule/pull/1), reviewed at `f91d18d46e5661ea9e205eee9d854d965c4c99bc`, with a PC-local TJR1/TJR2 receiver. The additional tongue directions draw on UndefinedSignal's [MouthLink contribution](https://github.com/WolalaQAQ/PicoET-Enhance/issues/5). The old `Sdk/TongueReceiver.cs`, headset subscription, in-module ORT and native/model maximum merge are not included. No unrelated compatibility/CI changes are part of this branch.
