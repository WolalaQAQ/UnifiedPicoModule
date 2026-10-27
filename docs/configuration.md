# Configuration reference

English | [简体中文](configuration_zh.md) · [README](../README.md)

This page describes every field of `UnifiedPicoModule.json` (`ConfigVersion` 1), its valid range, how invalid values are validated and replaced, and how earlier configurations are migrated. Post-processing rules are only summarized here; for the parameters and formulas of each rule, see the [post-processing reference](postprocessing.md).

## Configuration file

The module reads `UnifiedPicoModule.json` from the directory that contains the module DLL, regardless of the process working directory. When the module is installed through VRCFT, this directory is:

```
%APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442\
```

[`config/UnifiedPicoModule.example.json`](../config/UnifiedPicoModule.example.json) in the repository is a complete default configuration exported from the configuration model. It can be copied into that directory, renamed to `UnifiedPicoModule.json` and edited.

General rules:

- The file is standard JSON. Comments are not allowed, and field names are case-sensitive.
- Enum values are written as strings, for example `"Source": "Bridge"`. Integer values are not accepted.
- The configuration is read only when the module initializes. Restart VRCFT after editing; hot reloading is not supported.
- If the file does not exist, the module writes a default configuration to that directory. If the file cannot be written, the module uses in-memory defaults and logs the reason.
- The module never rewrites an existing configuration file, even when some of its fields are replaced by defaults.

Personal configurations and migration reports can contain IP addresses or local paths. See [Privacy and network security](privacy.md) before sharing them.

## Top-level fields

| Field | Type | Default | Valid values | Description |
|---|---|---|---|---|
| `ConfigVersion` | integer | `1` | `1` only | Configuration format version |
| `Source` | enum | `Auto` | `Auto`, `Bridge`, `Streaming` | How the data source is selected; see [Source selection](#source-selection) |
| `DisableEyeTracking` | boolean | `false` | | Disables eye tracking output |
| `DisableFaceTracking` | boolean | `false` | | Disables expression output |
| `Bridge` | object | see [Bridge](#bridge) | | Receiver settings for PicoFacialBridge and PicoFacialDataDaemon (port 9030) |
| `Streaming` | object | see [Streaming](#streaming) | | Receiver settings for streaming formats (ports 29765 and 29763) |
| `Tongue` | object | see [Tongue](#tongue) | | PC tongue results on IPv4 loopback |
| `EyeGain` | object | all `1` | | Gain for gaze and openness of each eye |
| `Stale` | object | see [Stale](#stale) | | Data expiry and the neutral pupil value |
| `AllowFallback` | boolean | `false` | | Whether to switch entirely to the other source when the current one becomes unavailable |
| `AutoSelectionWindowMs` | integer | `1500` | 100–60000 | How long `Auto` waits preferentially for Bridge after startup (ms) |
| `LegacyExpressionCeiling` | boolean | `false` | | Caps expression output at 0.99 |
| `PostProcessing` | object | only `ChannelGain` enabled | | Expression post-processing rules; see [Post-processing](#post-processing) |
| `Migration` | object or `null` | `null` | | Provenance written by the migration tool; not used at runtime |

If `ConfigVersion` is missing, duplicated or not 1, the whole configuration is replaced by defaults. Numeric fields outside their range fall back to the default for that field, and the replacement is logged.

### Source selection

`Source` determines which receivers are started:

| Value | Receivers | Selection |
|---|---|---|
| `Auto` | Bridge and Streaming | Only Bridge is accepted during the first `AutoSelectionWindowMs`; after that, whichever source first delivers usable data is selected |
| `Bridge` | Bridge only | Bridge is always used |
| `Streaming` | Streaming only | Streaming is always used, without waiting |

Usable data means data that arrived within the `Stale` limits and contains fields the module can output under the current output toggles. Control heartbeats, packets in which every field is invalid, and packets that merely have an acceptable length do not cause a source to be selected.

Once a source is selected, it stays selected for the rest of the session, even if the other source starts sending data later. The module never combines the two sources, for example eye data from Bridge with expressions from Streaming.

When `AllowFallback` is `true` and the current source is no longer usable while the other one is, the module switches entirely to the other source. Both receivers run only in `Auto` mode, so this field has an effect only when `Source` is `Auto`.

### Eye and face toggles

`DisableEyeTracking` and `DisableFaceTracking` are combined with the outputs VRCFT makes available: the module writes only the parts that VRCFT allows and that are not disabled, and never writes channels it does not own. When both are `true`, no receivers are started.

With BridgeSplit, the toggle state is sent to the headset as a subscription request, and the headset sends only the subscribed channels. With the other formats, the toggles affect local output only; the sender continues to send all data.

Eye-only BridgeSplit subscriptions still include Blink and other eye expressions, so eyelid tracking remains available.

## Bridge

The Bridge receiver accepts data from PicoFacialBridge (BridgeSplit) and PicoFacialDataDaemon (DaemonLegacy536) on port 9030. The port is fixed.

| Field | Type | Default | Valid values and fallback | Description |
|---|---|---|---|---|
| `IP` | string or `null` | `null` | Unicast IPv4 address; invalid values are treated as `null` | Headset address. `null` enables automatic discovery |
| `ListenAddress` | string | `0.0.0.0` | IPv4 address that is not multicast or broadcast; invalid values fall back to `0.0.0.0` | Local bind address |
| `Protocol` | enum or `null` | `null` | `BridgeSplit`, `DaemonLegacy536`; other values are treated as `null` | Fixed protocol. With `null`, the first valid packet determines the protocol |
| `ReconnectMs` | integer | `2000` | 100–60000; out-of-range values fall back to 2000 | Interval between discovery requests while not connected (ms) |
| `ControlTimeoutMs` | integer | `30000` | 1000–120000; out-of-range values fall back to 30000 | Time without any control or data packet before the peer is considered gone (ms) |

With `IP` set to `null`, the module sends discovery requests to `239.255.255.250:9030` through every active, multicast-capable Ethernet and Wi-Fi adapter, and accepts replies only from the same subnet as one of those adapters. Adapters whose names indicate a VPN, virtual machine or tunnel are skipped. With `IP` set, discovery requests are sent by unicast to that address, only data from that address is accepted, and the subnet check is not applied. Setting `IP` is recommended when the PC has several network adapters or virtual adapters, or when more than one headset is on the network.

With `ListenAddress` set to `0.0.0.0`, the module listens on all local addresses. When it is set to a specific local address, discovery also uses only the adapter that owns that address. The bind address only controls where packets are received; it does not replace source checks.

`ReconnectMs` also defines the discovery reply window: replies are accepted only within that time (at least 1 second) after a discovery request was sent.

`ControlTimeoutMs` determines when the peer is considered offline. After the timeout, the module drops the current peer and starts discovery again, without sending a stop command to the peer. This is independent of data expiry (`Stale`): once data stops for 250 ms the connection state shows `MissingFrames`, but the connection is kept as long as control packets keep arriving. If the BridgeSplit mode announcement is not refreshed within the shorter of `ControlTimeoutMs` and 20 seconds, the module withdraws the confirmed enhanced capabilities. These timeouts retain output by default; `Stale.NeutralizeOnTimeout=true` enables neutralization.

With `Protocol` set to `BridgeSplit`, only split packets that match the acknowledged subscription are accepted. With `DaemonLegacy536`, the module does not send subscription requests and accepts only 536-byte packets. For the packet structures, see the [protocol reference](protocol.md).

## Streaming

The Streaming receiver handles the streaming formats used by senders such as PICO Connect. The ports are fixed: 29765 for the format with a 16-byte header, and 29763 for the headerless format.

| Field | Type | Default | Valid values and fallback | Description |
|---|---|---|---|---|
| `ListenAddress` | string | `0.0.0.0` | Same as `Bridge.ListenAddress` | Local bind address |
| `LocalOnly` | boolean | `true` | | Accept data only from the local machine |
| `IP` | string or `null` | `null` | Unicast IPv4 address; invalid values are treated as `null` | Accept data only from this address |
| `Protocol` | enum or `null` | `null` | `StreamingHeader16`, `StreamingBodyOnly`; other values are treated as `null` | Fixed protocol; also determines which ports are opened |
| `SessionTimeoutMs` | integer | `2000` | 100–60000; out-of-range values fall back to 2000 | Time without valid data before the session ends (ms) |
| `HeaderProfile` | object or `null` | `null` | see [HeaderProfile](#headerprofile) | Strict header validation for the 29765 format |

`Protocol` and the ports it opens:

| `Protocol` | Ports | Acceptance |
|---|---|---|
| `null` | 29765 and 29763 | The first valid packet determines the protocol; the other protocol is rejected until the session ends |
| `StreamingHeader16` | 29765 | Datagram of at least 908 bytes with `trackingType` 2 in the header |
| `StreamingBodyOnly` | 29763 | Datagram of exactly 892 bytes |

A 29765 datagram consists of a 16-byte header followed by the body. The module reads the 892 bytes after the header and ignores any trailing bytes; PICO Connect 10.6.6 was measured sending 912 bytes (16 + 896). Without a `HeaderProfile`, the start code, subtype, fragment and version fields of the header are not checked, which matches the acceptance logic of SAFT and the pike branch. The validity floats in this format are not used; an expression value counts as valid when it is a finite number.

With `LocalOnly` set to `true`, only data from the loopback address or from an IPv4 address of an active local adapter is accepted. PICO Connect runs on the PC, so this normally does not need to change. If `IP` is also set to a non-local address, the streaming receiver fails to start and logs an error.

Once a session is established, the module locks onto the address and port of the first valid sender. When no valid data arrives for `SessionTimeoutMs`, the session ends, the sender and protocol locks are released, and the receiver waits for a new sender.

### HeaderProfile

`HeaderProfile` enables strict header validation for the 29765 format. The default is `null`; the module has no built-in start codes or version values.

| Field | Type | Valid values |
|---|---|---|
| `StartCode1` | integer | 0–255 |
| `StartCode2` | integer | 0–255 |
| `SubType` | integer | 0–255 |
| `MultiPack` | integer | 0–255 |
| `CurrentIndex` | integer | 0–255 |
| `Version` | integer | 0–65535 |

All six fields are required. If any field is missing or out of range, the whole `HeaderProfile` is treated as `null` and the problem is logged; reception returns to the default acceptance described above, and port 29763 is not affected.

With a complete `HeaderProfile`, a 29765 datagram must meet the length and `trackingType` conditions and, in addition, all six header fields must match the configured values exactly. The module does not reassemble fragmented data. For the header field offsets, see the [protocol reference](protocol.md#streamingheader16-header).

## Tongue

`Enabled` defaults to `true`. `Port` defaults to `45682`, accepts 1–65535, and falls back to 45682 for invalid values. The address is always `127.0.0.1`; the receiver opens only when this module owns face output. Missing settings use these defaults without rewriting an existing file. PC tongue results always expire within 350 ms, independently of `Stale.NeutralizeOnTimeout`. See [PC tongue setup and protocol](../TONGUE.md).

## Eye resolution and gain

Gaze and eyelids are always derived from the expression model. A sender's native eye packets are used only for a measured pupil diameter.

The left eye X axis is `EyeLookIn − EyeLookOut`, the right eye X axis is `EyeLookOut − EyeLookIn`, and the Y axis of both eyes is `EyeLookUp − EyeLookDown`; the origin is `blendshape-derived`. Each eye needs all four of its `EyeLookIn`, `EyeLookOut`, `EyeLookUp` and `EyeLookDown` shapes; this is an approximation from expression differences and carries no vergence information.

Openness is `1 - EyeBlink` of the same eye, with origin `blendshape-blink`. VRCFT computes eyelid closure as `EyeClosed = 1 − Openness`.

Missing, stale or invalid values hold that eye's last valid gaze or openness; before any valid value, gaze is centered and openness is 1. Optional timeout neutralization is configured under [Stale](#stale).

Independent-expression `X` packets carry a quality state per eye. A state of 0 holds only that eye's previous output; -1 is unknown and retains the supplied data. The first `X` sample for an eye replaces any previously held ordinary value instead of inheriting it, and after that the eye keeps its own latest valid value.

Pupil diameter comes from native eye packets: a valid measured value between 1 and 12 mm is used when present and retained during invalidity by default. `Stale.NeutralPupilMillimeters` is used before any measurement, after an output reset, or after expiry when timeout neutralization is enabled. The module does not estimate pupil size from openness or other data.

With `Source=Auto`, a native eye packet counts as usable when it contains a valid measured pupil, and shape data counts when it can produce expressions or derive gaze and openness.

The six `EyeGain` fields apply to the gaze X and Y axes and the openness of each eye:

| Field | Type | Default | Valid values |
|---|---|---|---|
| `LeftX`, `LeftY`, `RightX`, `RightY` | number | `1` | 0–10 |
| `LeftOpenness`, `RightOpenness` | number | `1` | 0–10 |

Out-of-range or non-finite values fall back to 1. After the gain is applied, gaze is limited to −1 to 1 and openness to 0 to 1. The gain does not apply to pupil diameter, and it may stack with VRCFT's own parameter adjustments.

`LeftOpenness` and `RightOpenness` multiply openness. With a gain above 1, clamping can keep the output fully open during slight closure; keep these at 1 when investigating eyelid response.

## Stale

| Field | Type | Default | Valid values | Description |
|---|---|---|---|---|
| `NeutralizeOnTimeout` | boolean | `false` | `true` / `false` | Replace expired output with neutral values; when disabled, retain the last output |
| `EyeMs` | integer | `250` | 1–5000 | How long a measured pupil from native eye data remains valid (ms) |
| `FaceMs` | integer | `250` | 1–5000 | How long expression data remains valid (ms) |
| `NeutralPupilMillimeters` | number | `3.5` | 1–10 | Pupil diameter output when no valid measurement is available (mm) |

Out-of-range values fall back to the defaults. Ages are measured from the time the PC received the data; sender timestamps are not used.

`NeutralizeOnTimeout` defaults to `false`, including in existing configurations that omit it. Expired gaze, openness and pupil measurements retain their last valid values, and expired expressions retain their last output. With `true`, expression output goes to zero after `FaceMs`; gaze returns to center and openness becomes 1 after `FaceMs`; measured pupil diameter returns to `NeutralPupilMillimeters` after `EyeMs`.

The freshness limits still apply to source selection and accepting input, and stale input resets post-processing timing state. Connection and mode-announcement timeouts retain output when neutralization is disabled. A new session, source or mode, module deactivation, and teardown reset output. An explicitly invalid region in an ordinary expression frame clears its mapped channels. In independent `X` frames, an eye with quality state 0 instead retains its own independent history, subject to timeout neutralization; before that eye has a valid independent sample, its expression channels are zero, gaze is centered and openness is 1.

The `MissingFrames` state in the log always appears 250 ms after data stops and is not affected by these settings.

## Legacy compatibility options

| Field | Default | Effect |
|---|---|---|
| `LegacyExpressionCeiling` | `false` | When enabled, expression output is capped at 0.99 instead of 1. Gaze and openness are not affected |

This option brings migrated behavior closer to the earlier modules. The migration tool enables it for `saft-ab500f03`. New installations do not need it.

## Post-processing

`PostProcessing` contains 14 rules. Each rule is an object with an `Enabled` field. Rules run in a fixed order that cannot be configured. Rules omitted from the configuration keep their default state, and parameters omitted within a rule use their default values.

Only `ChannelGain` is enabled by default. Its fields are:

| Field | Type | Default | Valid values | Description |
|---|---|---|---|---|
| `Enabled` | boolean | `true` | | Whether the rule is enabled |
| `DefaultGain` | number | `1` | 0–10 | Gain for channels not listed in `Gains` |
| `Gains` | object | `EyeWideLeft` and `EyeWideRight` 2; `EyeSquintLeft`, `EyeSquintRight` and `JawOpen` 1 | 0–10 each | Per-channel gain |

When `Gains` is written in the configuration, it replaces the default channel list as a whole, and channels not listed (including EyeWide) use `DefaultGain`. The keys of `Gains` are expression channel names and are matched case-insensitively; for the available channels, see the [post-processing reference](postprocessing.md#available-channels). If a key is not recognized, is duplicated (ignoring case), or has an out-of-range value, the entire `ChannelGain` rule is disabled. With `ChannelGain` disabled, every channel is output at a gain of 1, which also removes the default EyeWide ×2 compensation.

The other 13 rules are disabled by default. For their parameters, ranges and execution order, see the [post-processing reference](postprocessing.md).

## Migration

`Migration` is written by the migration tool to record where a configuration came from. It is not read at runtime. When writing a configuration by hand, leave it as `null`.

| Field | Type | Description |
|---|---|---|
| `SourceFile` | string | Absolute path of the earlier configuration file |
| `SourceVersion` | string | Source version specified for the migration |
| `ImportedAtUtc` | string | Migration time (UTC, ISO 8601) |
| `Differences` | array of strings | The same difference entries as the migration report |

`SourceFile` and `Differences` can contain user names, IP addresses and similar information. Review or remove them before sharing a configuration.

## Validation and error handling

Fields are validated individually; an error in one field does not invalidate the rest of the configuration.

| Condition | Handling |
|---|---|
| File does not exist | A default configuration file is written; in-memory defaults are used if writing fails |
| File cannot be read, is not valid JSON, or its root is not an object | Defaults are used for everything; the file is left unchanged |
| `ConfigVersion` missing, duplicated or not 1 | Defaults are used for everything |
| Unknown field | Ignored and logged |
| Duplicate field within the same object | The field is not used |
| Wrong type, including unknown enum strings, integer enum values and `null` for an object field | The field is not used and keeps its default |
| Number out of range or invalid address | Falls back to the default for that field and is logged |
| Any `HeaderProfile` field missing or out of range | The whole `HeaderProfile` is treated as `null` |
| A post-processing rule appears twice, or contains an unknown or duplicated parameter, a wrong type or an out-of-range value | Only that rule is disabled; other rules are unaffected |
| Unknown rule name in `PostProcessing` | Ignored and logged |
| `PostProcessing` is not an object | All rules are disabled, including `ChannelGain` |

IP and bind addresses accept IPv4 literals only; host names and IPv6 are not supported. Multicast addresses (first octet 224 or higher) and `255.255.255.255` are rejected, and `0.0.0.0` is allowed only as a bind address.

After editing a configuration, it can be checked offline with the `validate` command of the configuration tool. The command only reads the file and never creates or rewrites a configuration. See the [configuration tool guide](../tools/ConfigTool/README.md) for usage.

## Migrating earlier configurations

Use `UnifiedPicoConfigTool` with an earlier configuration file and its source version to generate a new configuration and difference report. Review the report, copy the new configuration to `UnifiedPicoModule.json` in the module directory, and restart VRCFT. The earlier file is preserved.

| Source version | Earlier configuration file | Main resulting settings |
|---|---|---|
| `bridge-current-0.1-beta-split-fix1` | `PicoFacialDataModule.json` from the split fork of PicoFacialDataModule | `Source=Bridge`, `Bridge.Protocol=BridgeSplit`; `ShapeGain` becomes `ChannelGain` |
| `bridge-upstream-0ab66348` | `PicoFacialDataModule.json` from upstream PicoFacialDataModule | `Source=Bridge`, `Bridge.Protocol=DaemonLegacy536` |
| `saft-ab500f03` | `PicoModuleConfig.json` from SAFT | `Source=Streaming`, `Streaming.Protocol=StreamingHeader16`, `LegacyExpressionCeiling=true`; `scales` is split into `ChannelGain` and `EyeGain` |
| `pike-c026aa4` | `config.ini` from the pike branch | `Source=Auto`; `eye_gain` becomes `EyeGain`, and `mode` is expanded into post-processing rules |

Both Bridge source versions migrate `DisableEyeTracking`, `DisableFaceTracking` and `IP`; pike's `eye-tracking` and `expression-tracking` map to the same two toggles.

The earlier gaze and openness source choices are not imported; the migrated configuration uses the expression-driven behavior described under [Eye resolution and gain](#eye-resolution-and-gain), and the difference is recorded in the migration report.

The migrated configuration differs from a fresh default configuration in the following ways:

- Migration does not add the EyeWide ×2 compensation of a fresh installation. `ChannelGain` is enabled only when the earlier configuration contains a valid, enabled gain setting (`ShapeGain`, `scales` or pike's `mode`), and it then uses the migrated gains.
- SAFT migration uses `StreamingHeader16`, opening only port 29765, with `HeaderProfile=null`.
- The `TrackingSettings` thresholds and `BridgeMode` from earlier Bridge configurations are not migrated; they are only recorded in the report.

pike's `mode` is expanded into explicit parameters according to this project's rules; there is no runtime preset:

| `mode` | Result |
|---|---|
| `0` | No post-processing rules are enabled |
| `1` | Enables `ChannelGain`, `FrownJawAttenuation`, `UpperUpPressGate`, `PuckerFunnelAssist`, `SmileRollSuppression`, `TongueJawGate` and `Symmetry` (group `Smile`); the gating rules use a `TransitionWidth` of 0 |
| `2` | Everything in `mode 1`, with `Symmetry` extended to the groups `Smile`, `Frown`, `Stretch` and `UpperUp`; additionally enables `BrowInnerSeparation` and `ShrugToFrown` (`Range` 0.05) and adds further channel gains |

The `ChannelGain` for `mode 1` and `mode 2` sets `JawOpen` to 1/0.75; `JawLeft`, `JawRight`, `MouthUpperLeft`, `MouthUpperRight`, `MouthLowerLeft`, `MouthLowerRight`, `MouthCornerPullLeft`, `MouthCornerPullRight`, `MouthCornerSlantLeft` and `MouthCornerSlantRight` to 2; and `MouthUpperUpLeft` and `MouthUpperUpRight` to 1.25. `mode 2` also sets `MouthClosed` to 1/0.65, `MouthStretchLeft` and `MouthStretchRight` to 1/0.3, and `BrowInnerUpLeft`, `BrowInnerUpRight`, `BrowOuterUpLeft` and `BrowOuterUpRight` to 2. A missing `mode` is treated as 2; an invalid or duplicated `mode` leaves all post-processing rules disabled.

Behavior of the earlier modules that has no equivalent, such as pike's nonlinear brow and squint curves and cheek-puff frame smoothing, or SAFT's 0.99 limits on gaze and openness, is listed item by item in the report. For command syntax, exit codes and output file rules, see the [configuration tool guide](../tools/ConfigTool/README.md).
