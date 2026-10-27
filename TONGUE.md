# PC tongue receiver — development branch

English | [简体中文](TONGUE_zh.md)

Use `feat/tongue-pc` in both this repository and [PicoET-Enhance](https://github.com/WolalaQAQ/PicoET-Enhance/tree/feat/tongue-pc). The [sender guide](https://github.com/WolalaQAQ/PicoET-Enhance/blob/feat/tongue-pc/TONGUE.md) covers camera source, PC app, models and personal calibration. This module receives only numeric results; it does not run inference or start the camera.

## Build and use

1. Prepare the pinned official VRCFT SDK and build using [Development](docs/development.md). The dependency remains VRCFT 5.4.5 / .NET 10.
2. With VRCFT closed, install your built module following [Installation](README.md#installation). Keep your existing configuration; do not load two copies of this module.
3. Let VRCFT assign face output to the PICO module when testing PICO tongue tracking. The PC app does not select or override the mouth source.
4. Enable the app's local UDP output to `127.0.0.1:45682`, then start live input. `Tongue.Port` must match the app's output port.

The configuration section defaults to:

```json
"Tongue": { "Enabled": true, "Port": 45682 }
```

`Port` accepts 1–65535; invalid values fall back to 45682. Omitted settings use defaults without rewriting an existing file. `Enabled=false` disables the receiver. The bind address is always IPv4 loopback. Bind failure is logged and leaves normal PICO tracking running. Configuration changes take effect on module initialization.

Only `ownsFace && Tongue.Enabled` opens the socket. With Cymple mouth + PICO eyes, the PICO module opens no tongue port and writes no foreign mouth/tongue fields. Keep that ownership split in the host.

## Output behavior

| PC result | Module output |
|---|---|
| Fresh present | Replace Out / Left / Right with the PC values |
| Fresh absent | Set all three to zero, even if native Out is positive |
| Uncertain | Hold a prior positive for at most 100 ms, bounded by its original deadline |
| Invalid, stop, expired or missing | Restore this frame's native pipeline Out and clear Left / Right |

There is no `max(native, model)` merge. Up / Down are neither supplied nor written. `LegacyExpressionCeiling` applies to all three PC channels. Main-source hold-last settings do not disable mandatory tongue expiry. UDP threads update state only; the existing Write boundary remains the sole SDK writer. Pause, Update failure and teardown cancel and join the worker before releasing its socket.

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

## Offline checks

After `tools/build.ps1` prepares `.sdk/`:

```powershell
./tools/test-tongue.ps1 -ArtifactsPath artifacts/tongue-check
python tools/test-tongue-wire.py --dll artifacts/tongue-check/bin/TongueTests/release/TongueTests.dll --pc-wire ../PicoET-Enhance/pc-tongue/wire.py --output artifacts/tongue-wire
```

The first command tests the production parser/state machine, real SDK Write ownership, loopback UDP and lifecycle. The second uses the matching app's actual Python encoder and the isolated C# probe, including a foreign-face ownership case. Both use ephemeral loopback ports, not a running VRCFT installation. Use a new output directory for each wire check.

These checks do not establish worn-person accuracy or Avatar behavior. Test live tracking, calibration, retraining and native eye/mouth coexistence using the paired sender branch.

## Upstream basis

This replaces the receiver proposed by [PR #1](https://github.com/WolalaQAQ/UnifiedPicoModule/pull/1), reviewed at `f91d18d46e5661ea9e205eee9d854d965c4c99bc`, with a PC-local TJR1 receiver. The old `Sdk/TongueReceiver.cs`, headset subscription and native/model maximum merge are not included. No unrelated compatibility/CI changes are part of this branch.
