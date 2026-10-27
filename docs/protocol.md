# Protocol reference

English | [简体中文](protocol_zh.md) · [README](../README.md)

This document describes the four main UDP tracking formats and how the module connects to each sender. The additional loopback TJR1 tongue-result stream is specified in [PC tongue results](../TONGUE.md#tjr1-wire-format). The source code in `Protocol/`, `Transport/`, `Mapping/` and `Runtime/` is authoritative; it is not included in the module ZIP. For the full list of configuration fields, see the [configuration reference](configuration.md).

The module implements these formats independently. For sources and licensing, see the [third-party notices](../THIRD_PARTY_NOTICES.md).

## Overview

| Format (`InputProtocol`) | Sender | Local port | Datagram length | Content |
|---|---|---:|---|---|
| `BridgeSplit` | PicoFacialBridge 0.3.0 or later | 9030 | 83, 95, 151, 227; `X` v1: 111/243, v2: 131/263 | 19-byte common header followed by subscribed channels; `X` adds independent-eye data |
| `DaemonLegacy536` | PicoFacialDataDaemon | 9030 | 536 | 384-byte face prefix and 152-byte eye prefix |
| `StreamingHeader16` | PICO Connect (legacy face tracking protocol) | 29765 | At least 908 | 16-byte header and 892-byte body; trailing bytes are ignored |
| `StreamingBodyOnly` | Headerless streaming format | 29763 | 892 | Body only |

General conventions:

- All offsets are zero-based byte offsets from the start of the datagram. Multi-byte integers are read explicitly as little-endian, and floats are little-endian IEEE 754 binary32. Native struct marshalling is not used.
- Lengths must match exactly, except for `StreamingHeader16`, where the length is a lower bound.
- Ports are fixed and cannot be configured. Each port is bound exclusively. If an older module still holds a port, binding fails and the log reports the port and the error.
- The receive buffer is 2048 bytes, larger than every supported format.

## Verification status

The independent-expression `X` v2 path (PicoFacialBridge 0.5.0 with PicoET-Enhance 2.3.0) was verified on a PICO 4 Pro against official VRCFT 5.4.5 in an 84-second worn Virtual Desktop session: Cymple mouth with PICO eyes, and PICO eyes with face. The Bridge sent about 89.7 `X` packets per second, each with a new eye source. The `X` v1 acceptance below is the earlier recorded-input test.

On 2026-10-01, `BridgeSplit` passed a first round of hardware acceptance on a PICO 4 Pro, covering discovery, mode announcements, subscription and acknowledgement, all four effective channel combinations, epoch agreement, cache clearing on changes, return to neutral when data stops, and recovery after an application restart. On the same day, the 29765 path passed acceptance with PICO Connect 10.6.6 (`faceTrackingTransferProtocol=2`). The measured datagrams were 912 bytes long (a 16-byte header and an 896-byte body).

After a full headset reboot, starting Bridge and reconnecting the PC has also been verified. The following have not been verified: strict `HeaderProfile` mode, the 29763 path, other senders and versions, `DaemonLegacy536`, PicoFacialBridge normal mode, and long-term stability.

The independent-expression `X` v1 extension (PicoFacialBridge 0.4.0 with PicoET-Enhance 2.2.0) was verified against official VRCFT 5.4.5. Eye images recorded earlier were replayed through native eye inference on the headset, and the PC received `E` and dual-channel `X` packets whose values matched the source outputs, with gaze and eyelids derived from expressions and each eye's own validity. The recording was about 23.7 Hz and replayed in an accelerated loop, so this shows inference throughput and transport rather than a real 90 Hz camera sequence. Long live-camera use and single-eye quality failure (state 0) have not been verified.

## Bridge connection flow

`BridgeSplit` and `DaemonLegacy536` share port 9030 and the same discovery and session mechanism. Bridge is a single-client session, not a relay.

### Control messages

Control messages are ASCII and are matched byte for byte. Apart from the single trailing zero byte of `MARCO`, they have no terminator.

| Message | Bytes | Direction | Purpose |
|---|---:|---|---|
| `DISCOVER_DAEMON` | 15 | PC → headset | Discover and request a session |
| `MARCO\0` | 6 | Headset → PC | Heartbeat |
| `POLO` | 4 | PC → headset | Heartbeat reply |
| `PXR_MODE ...` | 10–256 | Headset → PC | Mode announcement, BridgeSplit senders only |
| `PXR_SUB id=... mask=...` | 34 | PC → headset | Channel subscription |
| `PXR_SUB_ACK id=... mask=... epoch=...` | 61 | Headset → PC | Subscription acknowledgement |
| `STOP` | 4 | PC → headset | End the current session |

### Discovery

While no peer is locked, the module sends `DISCOVER_DAEMON` every `Bridge.ReconnectMs` (2000 ms by default):

- If `Bridge.IP` is not set, the request goes to the multicast address `239.255.255.250:9030` with a TTL of 1, sent once on each eligible network adapter. An eligible adapter is up, supports multicast, is of type Ethernet or Wi-Fi, has a non-loopback IPv4 address, and does not contain virtual, vpn, tunnel, wireguard, zerotier, tailscale, tap-, hyper-v, vmware or virtualbox in its name or description. If `Bridge.ListenAddress` is not `0.0.0.0`, only the adapter with that address is used.
- If `Bridge.IP` is set, the request is sent by unicast to port 9030 at that address, and adapter filtering no longer applies.

If no adapter is eligible, the log suggests configuring `Bridge.IP` explicitly.

### Peer locking

The module accepts only IPv4 unicast datagrams whose source port is 9030. While no peer is locked, the following conditions also apply:

- if `Bridge.IP` is set, the source address must match it;
- otherwise, the source address must be on the same subnet as one of the discovery adapters and must not be that adapter's own address;
- the datagram must arrive within the response window after the most recent discovery request, which is `max(1 s, ReconnectMs)` long.

When these conditions are met, a `MARCO\0`, a valid `PXR_MODE` or usable tracking data locks the peer's full IP address and port. From then on, only datagrams from that endpoint are accepted. Every lock generates a new 16-digit hexadecimal subscription id, and the state becomes `MissingFrames` until data arrives. The module replies to `MARCO\0` with `POLO`.

### Mode announcement

A BridgeSplit sender sends `PXR_MODE` immediately after locking, then every 10 seconds and whenever the mode changes:

```text
PXR_MODE mode=<normal|enhance> rooted=<0|1> enhance=<0|1> gate=<0|1> plugin=<off|left|right|dual>
```

The angle-bracketed parts are syntax placeholders. The receiver parses the line as follows:

- the message may contain only printable ASCII characters, is 10 to 256 bytes long, and separates key-value pairs with spaces;
- it must contain exactly the five keys `mode`, `rooted`, `enhance`, `gate` and `plugin`, without duplicates; `mode` must be `normal` or `enhance`, and the three flags must be `0` or `1`;
- enhanced mode is recognized only when `mode=enhance` and all three flags are `1`; independent per-eye gaze is recognized only when `plugin=dual` in enhanced mode;
- an unrecognized `plugin` value is treated as `off`, and `left` or `right` does not indicate independent per-eye gaze.

A malformed announcement is discarded and revokes any previously recorded capabilities. When capabilities change, the module advances the Generation and clears cached eye and expression data. If no new announcement arrives within `min(20000 ms, ControlTimeoutMs)`, capabilities are downgraded to unknown and the log shows `Mode advertisement expired; capabilities downgraded`.

Once the session is locked to `DaemonLegacy536`, announcements grant no capabilities.

### Subscription and acknowledgement

The module treats a peer as a BridgeSplit sender, and sends a subscription, only after receiving a valid `PXR_MODE`. The legacy daemon treats any reply other than `POLO` during a heartbeat as a failed heartbeat, so the module never sends `PXR_SUB` to a peer that has not announced its mode. When `Bridge.Protocol` is pinned to `DaemonLegacy536`, no subscription is sent either.

```text
PXR_SUB id=<16 lowercase hex digits> mask=<requested channels>
PXR_SUB_ACK id=<same id> mask=<effective channels> epoch=<16 lowercase hex digits>
```

In the channel mask, eye is 1, face is 2, and both together are 3. The requested mask contains the channels that the VRCFT host makes available and that the configuration does not disable. If neither channel is available, the module starts no data source.

Until an acknowledgement arrives, the module resends the subscription once per second, and also once on every mode announcement. Subscription requests are idempotent, which recovers a lost request or acknowledgement.

An acknowledgement is discarded unless all of the following hold:

- it is exactly 61 bytes long, with the field order and single-space separation shown above;
- it comes from the locked peer and its id matches the current subscription id;
- the effective mask is 0 to 3 and is a subset of the requested mask;
- the epoch is a positive signed int64;
- once subscribed, the epoch does not decrease, and the effective mask does not change while the epoch stays the same.

The effective mask is the intersection of the headset's local switches and the PC's request. When the epoch or mask in an acknowledgement changes, the module advances the Generation, clears eye, expression and post-processing timing state, and locks the protocol to `BridgeSplit`. With an effective mask of 0, the headset pauses data transmission while heartbeats and mode announcements continue, and the state is `MissingFrames`.

### Data reception

The headset sends no tracking data before the subscription is acknowledged. A BridgeSplit datagram must carry the same epoch and effective mask as the most recent acknowledgement; otherwise it is discarded and the subscription is resent. In-flight data from an earlier epoch therefore cannot revive a disabled channel.

The protocol is locked for the session. If `Bridge.Protocol` is set, other formats are rejected. Once the first validated format is established, datagrams in the other format are discarded until the next session.

### Timeout, reconnection and stop

| Situation | Module behavior |
|---|---|
| No data for more than 250 ms while the peer still sends control messages | State becomes `MissingFrames`; subscription and lock are kept |
| No heartbeat, mode announcement, acknowledgement or data for longer than `ControlTimeoutMs` (30000 ms by default) | No `STOP` is sent; the lock and subscription are cleared, the state becomes `Searching`, and discovery starts immediately |
| An acknowledgement with a lower epoch arrives while subscribed, and no data has been accepted for more than 1 second | The lock is released and discovery restarts with a new id |
| The module stops or is disposed | A single `STOP` is sent to the currently locked peer only |

When the Bridge application restarts, it forgets the locked endpoint and sends nothing until it receives a new discovery request. The PC side can only rediscover after `ControlTimeoutMs` expires, so the recovery time depends mainly on this timeout. In the application restart test on 2026-10-01, the headset UI returned to the running state in about 6 seconds, and the PC side re-subscribed after about 33 seconds.

The epoch is based on the headset's monotonic uptime and may decrease after a full reboot. The normal recovery path goes through the timeout and rediscovery first, by which point the recorded epoch has been cleared. Lower epochs received within a subscribed session are handled as shown in the table above.

`STOP` is sent during shutdown. If the state lock cannot be acquired within 200 ms, the module closes its sockets directly.

## BridgeSplit packet formats

### Common header (19 bytes)

| Offset | Size | Field |
|---:|---:|---|
| 0 | 1 | Tag: ASCII `E`, `A`, `F` or `X` |
| 1 | 1 | Effective channel mask, 1 to 3 |
| 2 | 1 | Morphology validity: eye is 1, face is 2; must be a subset of the mask |
| 3 | 8 | Epoch, positive signed int64 |
| 11 | 8 | Source timestamp, positive signed int64; `A`, `F` and `X` v1 use the face source time. `X` v2 uses the left-eye source time, allowing 0 only when that group is not updated |

### Datagram types

| Tag | Length | Effective mask | Content |
|---|---:|---|---|
| `E` | 83 | Includes eye (1 or 3) | Native eye data; the module outputs its measured pupil diameter |
| `A` | 95 | Eye only (1) | 19 eye-region shapes |
| `F` | 151 | Face only (2) | 33 non-eye shapes |
| `F` | 227 | Eye and face (3) | All 52 shapes |
| `X` | 111 | Eye only (1) | Same 19 eye-region shapes as `A`, followed by a 16-byte independent-eye extension |
| `X` | 243 | Eye and face (3) | Same 52 shapes as `F`, followed by that extension |
| `X` | 131 | Eye only (1) | Same 19 shapes, with the 36-byte v2 extension |
| `X` | 263 | Eye and face (3) | Same 52 shapes, with the 36-byte v2 extension |

The datagrams sent for each mask are: `E` and `A` for eye only; only `F` (151 bytes) for face only; `E` and `F` (227 bytes) for both, without `A`.

An independent-expression sender replaces `A` or the 227-byte `F` with `X`. Face-only transmission stays `F`; native `E` packets are unchanged.

### `X`: independent eye expressions

The extension begins at byte 95 for mask 1, or byte 227 for mask 3:

| Relative offset | Size | Field |
|---:|---:|---|
| 0 | 4 | uint32 version, 1 or 2 |
| 4 | 4 | float32 filtered right inner brow; standard slot 3 is the left inner brow |
| 8 | 4 | int32 left quality state: -1 unknown, 0 unusable, 1 usable |
| 12 | 4 | int32 right quality state, same values |

Version 1 ends after these 16 bytes. Version 2 appends:

| Relative offset | Size | Field |
|---:|---:|---|
| 16 | 4 | uint32 updated groups: bit 0 left eye, bit 1 right eye, bit 2 mouth |
| 20 | 8 | signed int64 mouth source timestamp |
| 28 | 8 | signed int64 right-eye source timestamp |

For v2, all three source timestamps must be nonnegative, and an updated group must have a positive timestamp. Mask 1 allows only update bits 0 and 1; mask 3 allows all three. An update mask of 0 carries no new observations.

All shapes and the right inner brow must be finite. Other versions, states, masks or lengths are rejected. Eye shapes use their own side's state rather than the combined header eye-valid bit; -1 retains the data, without being treated as a known good classification. Non-eye shapes still use the header face-valid bit.

Gaze and eyelids are expression-derived for `X` just as for ordinary packets; the quality states decide each eye's availability. An unusable eye holds its own last output, subject to the configured timeout behavior, never the other eye's values. Inner-brow separation is applied per side, and cross-eye BlinkAsymmetry is skipped.

Version 1 uses one timestamp and face-sequence update. Version 2 shares the epoch but accepts each updated group independently: a duplicate or backward source time rejects only that group. Only accepted groups advance their sequence, arrival time and post-processing. Eye-only updates do not refresh or reprocess cached mouth data. A new unusable-eye observation advances its source identity but not the last usable output's freshness. New invalid mouth observations still reach processing to invalidate that group. Changing between grouped and ungrouped expressions clears cached input and output state. Both endpoints must support the extension version; older decoders reject unsupported `X` packets.

### `E`: native eye data (83 bytes)

| Offset | Size | Field |
|---:|---:|---|
| 19 / 23 / 27 | 4 each | Status bits for the left eye, right eye and combined gaze, uint32 |
| 31 / 43 / 55 | 12 each | Gaze vectors for the left eye, right eye and combined gaze, float32 × 3 (X, Y, Z) |
| 67 / 71 | 4 each | Left and right eye openness |
| 75 / 79 | 4 each | Left and right raw pupil values |

Only the status bits `0x002`, `0x004`, `0x100` and `0x800` are allowed; any other bit causes the whole datagram to be discarded. All floats from offset 31 onward must be finite. The meaning of the status bits is described in [Status bits and units](#status-bits-and-units). The module consumes only the pupil diameter from this datagram; gaze and openness come from the expression shapes.

In normal mode, Bridge clears the per-eye gaze validity bits and keeps only the combined gaze. Per-eye gaze is kept only in enhanced mode with `plugin=dual`.

### `A`: eye-region shapes (95 bytes)

From offset 19, the datagram contains 19 float32 values for the following PICO shape slots:

| Slot | Name | Slot | Name |
|---:|---|---:|---|
| 0 | EyeLookDownL | 31 | EyeLookUpL |
| 2 | EyeLookInL | 35 | EyeLookUpR |
| 3 | BrowInnerUp | 36 | BrowOuterUpR |
| 4 | BrowDownR | 38 | EyeBlinkR |
| 11 | EyeLookInR | 41 | EyeSquintR |
| 12 | EyeLookDownR | 44 | EyeLookOutL |
| 16 | BrowDownL | 45 | EyeLookOutR |
| 26 | EyeSquintL | 46 | EyeWideR |
| 28 | EyeBlinkL | 47 | EyeWideL |
| 30 | BrowOuterUpL | | |

Slots not listed are treated as absent. Because the mask is 1, the face validity bit can never be set.

### `F`: expression shapes (151 or 227 bytes)

The 151-byte version contains 33 float32 values from offset 19, for slots 1, 5, 6, 7, 8, 9, 10, 13, 14, 15, 17, 18, 19, 20, 21, 22, 23, 24, 25, 27, 29, 32, 33, 34, 37, 39, 40, 42, 43, 48, 49, 50 and 51 in that order, which are all slots not in the table above. Eye-region slots are treated as absent.

The 227-byte version contains 52 float32 values from offset 19, in PICO slot order 0 to 51.

The names and order of the 52 slots are defined by `PicoShape` in `Core/Contracts.cs`.

### Validation and shape validity

If any shape value is non-finite, the whole datagram is discarded. Shapes have no per-slot presence flag: before sending, Bridge replaces an individual non-finite value with 0 without clearing the group validity, so the receiver cannot distinguish a measured 0 from a replaced non-finite value in that slot.

For ordinary packets, eye-region shapes (the 19 slots in the table above) are governed by the eye validity bit, and the remaining slots by the face validity bit. `X` uses each eye's quality state instead. Valid eye-region shapes can be used even when face validity is not set.

## DaemonLegacy536

| Offset | Size | Field |
|---:|---:|---|
| 0 | 8 | Face source timestamp, signed int64 |
| 8 | 288 | 72 shape float32 values; the first 52 are mapped, the remaining 20 are kept but not used |
| 296 / 300 | 4 each | Eye and face validity floats, valid only when exactly `1.0` |
| 380 | 4 | Padding, not an eye timestamp |
| 384 / 388 / 392 | 4 each | Status bits for the left eye, right eye and combined gaze |
| 432 / 444 / 456 | 12 each | Gaze vectors for the left eye, right eye and combined gaze |
| 468 / 472 | 4 each | Left and right eye openness |
| 476 / 480 | 4 each | Left and right raw pupil values |

Fields not listed are not mapped. A non-finite shape value marks that slot as absent without discarding the datagram. The eye section has no reliable source timestamp and is ordered by arrival.

This format carries no per-eye capability declaration. The module reads its measured pupil diameter; gaze and openness come from the expression shapes.

## Streaming formats

### Body layout (892 bytes)

| Body offset | Size | Field |
|---:|---:|---|
| 0 | 8 | Face source timestamp, signed int64 |
| 8 | 288 | 72 shape float32 values |
| 296 | 40 | 10 validity floats; the first two correspond to eye and face |
| 336 | 4 | laughing |
| 340 | 40 | 10 emotion floats |
| 380 | 512 | Reserved |

The first 52 shapes are mapped; the remaining 20 are kept but not output. The validity floats, laughing, emotion and reserved fields are not read. A finite shape value is treated as present and a non-finite value as absent.

The validity floats are not read because the measured PICO Connect 10.6.6 never sets them to `1.0`, while the shape values follow expressions in real time. SAFT and pike do not read these fields either.

The streaming formats contain no native eye data. Gaze and eye openness are derived from the `EyeLook` and `EyeBlink` shapes.

### StreamingHeader16 header

| Offset | Size | Field |
|---:|---:|---|
| 0 / 1 | 1 each | startCode1, startCode2 |
| 2 | 1 | trackingType, 2 for face data |
| 3 | 1 | subType |
| 4 / 5 | 1 each | multiPack, currentIndex |
| 6 | 2 | version, uint16 |
| 8 | 8 | Transport timestamp, uint64 |
| 16 | 892 | Body |

Body field offsets within the datagram are shifted by 16: the face timestamp is at 16 and the shapes start at 24. The transport timestamp in the header is not used as the face clock.

The first 8 header bytes measured from PICO Connect 10.6.6 are `00 33 02 01 00 01 01 00`, followed by an 8-byte timestamp.

### Receive rules

Port 29765 (`StreamingHeader16`):

- In default mode, a datagram is accepted if it is at least 908 bytes long and trackingType is 2. The body is the 892 bytes starting at offset 16, and trailing bytes are ignored. The other five header fields are not checked. This matches the checks made by SAFT and pike.
- Setting `Streaming.HeaderProfile` enables strict mode, which additionally requires startCode1, startCode2, subType, multiPack, currentIndex and version to match the configuration exactly. All six fields must be given; the first five are integers from 0 to 255 and version is an integer from 0 to 65535. If any field is missing or out of range, the whole profile falls back to default mode. Strict mode does not reassemble fragments.

Port 29763 (`StreamingBodyOnly`): datagrams must be exactly 892 bytes. The port and layout are taken from the pike and lonelyicer implementations, and no sender has been confirmed. On 2026-10-02, `faceTrackingTransferProtocol` in PICO Connect 10.6.6 was temporarily set to 1 and traffic was captured passively; no UDP traffic was observed on either 29763 or 29765.

Rules common to both ports:

- Both ports are monitored by default. When `Streaming.Protocol` specifies a format, only the corresponding port is bound.
- Only IPv4 unicast sources are accepted. `Streaming.LocalOnly` is `true` by default, in which case the source must be a loopback address or an IPv4 address of an active local adapter. If `Streaming.IP` is set, the source address must match it; with `LocalOnly` set to `true`, that address must also be local, otherwise startup fails with an error.
- The first datagram containing usable fields locks the source IP address and port as well as the format. A first datagram whose fields are all invalid does not establish a lock. After locking, structurally valid datagrams with invalid fields are still committed so that the output is invalidated promptly.
- On an exact `MARCO\0`, the module replies `POLO` to a sender that passes the source checks, but the heartbeat itself does not lock the source.
- With no data for more than 250 ms, the state becomes `MissingFrames`. After `Streaming.SessionTimeoutMs` (2000 ms by default), the lock and caches are cleared, the state becomes `Searching`, and the local addresses are read again.
- Streaming senders do not support remote pausing, so `DisableEyeTracking` and `DisableFaceTracking` affect only this module's output. The module does not send `STOP` to streaming senders.

## Eye resolution

### Status bits and units

These apply to the `E` datagram of `BridgeSplit` and to the eye section of `DaemonLegacy536`:

| Bit | Status word | Meaning | Additional condition |
|---:|---|---|---|
| `0x002` | Combined | Combined gaze valid | All three vector components are finite |
| `0x004` | Per eye | Openness valid | Finite and between 0 and 1 |
| `0x100` | Per eye | Per-eye gaze valid | All three vector components are finite |
| `0x800` | Per eye | Pupil diameter valid | Raw value divided by 10 gives millimeters; the result is positive and finite |

Native gaze vectors carry their direction in the X and Y components, without normalization; the module does not output them.

### Gaze and openness derivation

Gaze is always derived from the `EyeLook` shapes and openness from `1 - EyeBlink`. Deriving gaze requires all four of the eye's `EyeLookIn`, `EyeLookOut`, `EyeLookUp` and `EyeLookDown` shapes, and the origin is `blendshape-derived`:

- left X = `EyeLookInL - EyeLookOutL`, right X = `EyeLookOutR - EyeLookInR`;
- Y = `EyeLookUp - EyeLookDown`.

Openness is `1 - EyeBlink` of the same eye, with origin `blendshape-blink`. VRCFT outputs eyelid closure as `EyeClosed = 1 - Openness`.

A missing or invalid sample retains that eye's last valid value rather than substituting another source. Before any valid value, or after expiry with timeout neutralization enabled, the origin is `neutral`. For holding and output after invalidity, see [Timestamps, expiry and neutral output](#timestamps-expiry-and-neutral-output).

The result is multiplied by the corresponding `EyeGain` value; gaze is then limited to -1 to 1 and openness to 0 to 1.

### Pupil

A pupil diameter is used only when its status bit is valid, the converted value is between 1 and 12 mm, and the receiving packet is fresh. The last measurement is retained during invalidity by default. Before any measurement, after an output reset, or after expiry with timeout neutralization enabled, the output is `Stale.NeutralPupilMillimeters` (3.5 mm by default), shown as `estimated` in the log. The module does not derive pupil size from openness or shapes.

## Timestamps, expiry and neutral output

Timestamp rules:

- A signed int64 source timestamp is treated as a reliable clock only when positive. Legacy formats treat zero or negative values as missing timestamps and use arrival order. BridgeSplit requires positive timestamps, except for v2 groups not marked updated, which may use 0.
- Within one Generation, native-eye and expression timestamps are checked separately. `A`, `F` and `X` v1 share the face timestamp sequence; `X` v2 maintains separate left-eye, right-eye and mouth sequences. Duplicate or backward samples do not advance the affected group's arrival time, sequence or processing state.
- Freshness is based on the arrival time measured with the local monotonic clock. Control messages do not extend the freshness of data.

Native-eye data and each expression group arrive, are deduplicated and expire independently. The module does not interpolate or synthesize samples. A new native-eye sample does not cause cached expressions to be processed again. SDK updates reuse processed output between new group observations; only a pending `TongueHold` expiry can change mouth output without a new mouth sample.

`Stale.NeutralizeOnTimeout` defaults to `false`: when data expires, gaze, openness and pupil diameter retain their last valid values, and expressions retain their last output. Freshness checks and post-processing timing resets still apply.

Output after expiry with `Stale.NeutralizeOnTimeout=true`:

| Data | Expiry threshold | Output after expiry |
|---|---|---|
| Expressions | `Stale.FaceMs`, 250 ms by default | All channels return to zero; post-processing timing state is reset |
| Gaze and openness | `Stale.FaceMs`, 250 ms by default | Gaze returns to center; openness is 1 |
| Pupil | `Stale.EyeMs`, 250 ms by default | `Stale.NeutralPupilMillimeters` |

When no source is selected, the output is fully neutral. A new session, source or mode resets eye resolution and post-processing state. A Generation change caused solely by a connection or mode-announcement timeout retains eye and expression output when `NeutralizeOnTimeout=false`, while transport caches and post-processing timing state are still cleared. Module deactivation, teardown and update errors reset output.

Expression shapes are mapped to VRCFT expression channels through a fixed table, limited to 0 to 1, with no gain applied during mapping. For the correction rules applied after mapping, see the [post-processing reference](postprocessing.md). `DisableEyeTracking` and `DisableFaceTracking` mask the eye-region shapes and the remaining shapes respectively.

## SDK consumption and background timing

`groupRxHzLRM` reports accepted left-eye/right-eye/mouth reception rates. `CONSUMPTION` separates processed groups, intermediate observations coalesced by the latest snapshot, and SDK writes containing new groups from repeated writes of the current output. Timing histograms cover reception to processing and SDK writing, not host IPC, OSC or Avatar rendering.

Official VRCFT 5.4.5 requests a 1 ms timer period in its module process. While tracking, the module saves that process's Windows timer policy and disables only `IGNORE_TIMER_RESOLUTION`, so the request remains effective in the background. On deactivation, teardown or failure, it reads the current policy and restores only that bit's saved control/state settings, preserving all other current bits. If reading or writing fails, the saved snapshot remains available for a later retry. No foreground window, host modification, priority, CPU-throttling or power-plan change is needed. More frequent background wakeups can increase power use.

## Source selection

`Source` determines which data sources are created: `Bridge` creates only the Bridge source, `Streaming` only the streaming source, and `Auto` both.

A source in the `Faulted` or `Stopped` state is not usable. Any other source is usable when one of the following holds:

- eye tracking is enabled, native eye data is within its expiry limit, and it contains a valid measured pupil;
- fresh shape data contains mappable expressions, or, with eye tracking enabled, can be used to derive openness or gaze.

In `Auto` mode, only Bridge can be selected during the first `AutoSelectionWindowMs` (1500 ms by default) after startup. After that, the first source to become usable is selected. When Bridge and streaming are both usable, Bridge is preferred.

Once selected, a source remains in use for the rest of the session. With `AllowFallback` enabled, if the current source becomes unusable and the other source is usable, the module switches entirely to the other source. The module never combines data from both sources.

## Network security

UDP has no encryption or authentication. Source address checks, endpoint locking, and length and field validation reduce accidental reception but cannot defend against forged data on a trusted network. Use the module only on trusted networks, and do not expose ports 9030, 29765 or 29763 to the internet. See [Privacy and network security](privacy.md) for more information.

## Source evidence

The following links were used to check layouts and behavior. They do not indicate reuse of their implementation code:

- thoricelli [PicoFacialDataModule](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/PicoFacialDataModule/PicoFacialDataModule.cs) and [PxrEyePoseDataV2](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/PicoFacialDataModule/Models/PxrEyePoseDataV2.cs) (`0ab6634`): the 536-byte layout, eye status bits and units.
- regzo2 [SAFT LegacyConnector](https://github.com/regzo2/PicoStreamingAssistantFTUDP/blob/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7/PicoStreamingAssistantFTUDP/PicoStreamingAssistantFTUDP/PicoConnectors/LegacyConnector.cs) (`ab500f0`): the 29765 acceptance check, which tests trackingType only.
- pikepikeid [DataPacket](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/VRCFTPicoModule/Data/DataPacket.cs), [LegacyDataPacket](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/VRCFTPicoModule/Data/LegacyDataPacket.cs) and [Updater](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/VRCFTPicoModule/Utils/Updater.cs) (`c026aa4`): the 16 + 892 layout, the length lower bound and trackingType check, and port 29763. The module's default 29765 check matches it.
- WolalaQAQ [PicoFacialBridge TrackingData](https://github.com/WolalaQAQ/PicoFacialBridge/blob/836101c27feffccfbf7df6d94af9e98093f96057/shared/src/main/java/dev/pico/facialprobe/TrackingData.java) and [PeerProtocol](https://github.com/WolalaQAQ/PicoFacialBridge/blob/836101c27feffccfbf7df6d94af9e98093f96057/bridge/src/main/java/dev/pico/facialprobe/PeerProtocol.java) (`836101c`): this version still sends the 536-byte packet. It traces the origin of the control messages and prefix layout, but does not establish BridgeSplit or enhanced capabilities.
- The BridgeSplit offsets, subscription control, per-eye validity clearing and mode announcement semantics come from `TrackingData.java`, `TrackingMode.java`, `PeerProtocol.java` and `BridgeService.java` in PicoFacialBridge 0.3.0, and agree with the wire format description in its README.
