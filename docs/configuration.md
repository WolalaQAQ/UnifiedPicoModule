# Configuration Reference

**English** | [简体中文](configuration_zh.md) · [Back to README](../README.md)

This guide covers every field in `UnifiedPicoModule.json` (`ConfigVersion: 1`), practical setup recipes, validation behavior, and how earlier module configurations migrate. For formulas and tuning details of the 14 expression post-processing rules, see the [Post-Processing Reference](postprocessing.md).

## File Location & Basics

UnifiedPicoModule loads `UnifiedPicoModule.json` from the same folder as `UnifiedPicoModule.dll`, regardless of VRCFT's working directory:

```text
%APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442\UnifiedPicoModule.json
```

A clean default copy is also checked into the repository at [`config/UnifiedPicoModule.example.json`](../config/UnifiedPicoModule.example.json).

- **Standard JSON only:** No comments; field names are case-sensitive.
- **String enums:** Write enum values as strings (for example `"Source": "Bridge"`). Numeric enum values are rejected.
- **Restart to apply:** Config is loaded once during module initialization. Fully restart VRCFT after editing.
- **Non-destructive:** If the file is missing, the module writes defaults. If an existing file has errors or invalid fields, the module falls back in memory and logs what changed, without modifying your file on disk.

> [!TIP]
> Check your edits offline before restarting VRCFT by running `UnifiedPicoConfigTool validate UnifiedPicoModule.json`. See the [ConfigTool Guide](../tools/ConfigTool/README.md).

## Common Recipes

### 1. Pin Your Headset IP (Recommended When Using VPNs or Multiple Headsets)

```json
{
  "ConfigVersion": 1,
  "Source": "Bridge",
  "Bridge": {
    "IP": "192.168.1.50"
  }
}
```

### 2. PICO Eye Tracking Only (e.g., Mixed with Cymple Mouth Tracking)

```json
{
  "ConfigVersion": 1,
  "Source": "Bridge",
  "DisableEyeTracking": false,
  "DisableFaceTracking": true
}
```

When using `BridgeSplit`, the module subscribes only to eye channels so the headset skips mouth inference. Blink and eye expressions are still included, so eyelids keep working.

### 3. Reset Avatar to Neutral When Tracking Stops

```json
{
  "ConfigVersion": 1,
  "Stale": {
    "NeutralizeOnTimeout": true,
    "EyeMs": 250,
    "FaceMs": 250,
    "NeutralPupilMillimeters": 3.5
  }
}
```

### Complete Default Configuration

<details>
<summary>Expand full default <code>UnifiedPicoModule.json</code> (ConfigVersion 1)</summary>

```json
{
  "ConfigVersion": 1,
  "Source": "Auto",
  "DisableEyeTracking": false,
  "DisableFaceTracking": false,
  "Bridge": {
    "IP": null,
    "ListenAddress": "0.0.0.0",
    "Protocol": null,
    "ReconnectMs": 2000,
    "ControlTimeoutMs": 30000
  },
  "Streaming": {
    "ListenAddress": "0.0.0.0",
    "LocalOnly": true,
    "IP": null,
    "Protocol": null,
    "SessionTimeoutMs": 2000,
    "HeaderProfile": null
  },
  "EyeGain": {
    "LeftX": 1,
    "LeftY": 1,
    "RightX": 1,
    "RightY": 1,
    "LeftOpenness": 1,
    "RightOpenness": 1
  },
  "Stale": {
    "NeutralizeOnTimeout": false,
    "EyeMs": 250,
    "FaceMs": 250,
    "NeutralPupilMillimeters": 3.5
  },
  "AllowFallback": false,
  "AutoSelectionWindowMs": 1500,
  "LegacyExpressionCeiling": false,
  "PostProcessing": {
    "ChannelGain": {
      "Enabled": true,
      "DefaultGain": 1,
      "Gains": {
        "EyeWideLeft": 2,
        "EyeWideRight": 2,
        "EyeSquintLeft": 1,
        "EyeSquintRight": 1,
        "JawOpen": 1
      }
    },
    "BrowInnerSeparation": { "Enabled": false, "Strength": 1 },
    "BrowDownResponse": { "Enabled": false, "Center": 0.7, "Slope": 10, "Gain": 1, "BlinkAsymmetryStrength": 0 },
    "SquintResponse": { "Enabled": false, "Gain": 1, "Gamma": 1, "BlinkInfluence": 0 },
    "FrownJawAttenuation": { "Enabled": false, "JawThreshold": 0.1, "TransitionWidth": 0.1, "MaxReduction": 0.5 },
    "ShrugToFrown": {
      "Enabled": false,
      "Threshold": 0.6,
      "Range": 0.2,
      "Gain": 1,
      "RollBlockThreshold": 0.3,
      "PuckerBlockThreshold": 0.4,
      "FunnelBlockThreshold": 0.1,
      "TransitionWidth": 0.05,
      "ConsumeShrug": false
    },
    "UpperUpPressGate": { "Enabled": false, "PressThreshold": 0.1, "TransitionWidth": 0.1, "MaxReduction": 1 },
    "PuckerFunnelAssist": { "Enabled": false, "PuckerThreshold": 0.3, "PressThreshold": 0.2, "TransitionWidth": 0.1, "Strength": 1 },
    "SmileRollSuppression": { "Enabled": false, "RollThreshold": 0.2, "TransitionWidth": 0.1, "MaxReduction": 1 },
    "StretchCompetition": { "Enabled": false, "StretchThreshold": 0.05, "SmileCeiling": 0.4, "TransitionWidth": 0.1, "SmileReduction": 0.5, "UpperUpReduction": 0.5 },
    "Symmetry": { "Enabled": false, "Groups": ["Smile"], "Method": "Max", "Strength": 1 },
    "CheekAsymmetry": { "Enabled": false, "MouthDifferenceThreshold": 0.1, "TransitionWidth": 0.1, "Gain": 1, "MouthSmoothingMs": 80 },
    "TongueHold": { "Enabled": false, "HoldMs": 120, "DropThreshold": 0.15 },
    "TongueJawGate": { "Enabled": false, "JawThreshold": 0.5, "TransitionWidth": 0.1, "MaxReduction": 1 }
  },
  "Migration": null
}
```

</details>

---

## Top-Level Fields

| Key | Type | Default | Valid Values | What It Does |
|---|---|---|---|---|
| `ConfigVersion` | integer | `1` | `1` only | Schema version. If missing, duplicated, or not `1`, the entire config resets to defaults in memory |
| `Source` | string enum | `"Auto"` | `"Auto"`, `"Bridge"`, `"Streaming"` | Selects which UDP receivers start and how the active source is chosen |
| `DisableEyeTracking` | boolean | `false` | `true` / `false` | Disables eye tracking output |
| `DisableFaceTracking` | boolean | `false` | `true` / `false` | Disables facial expression output |
| `Bridge` | object | see [Bridge](#bridge) | — | Receiver settings for port `9030` (PicoFacialBridge / PicoFacialDataDaemon) |
| `Streaming` | object | see [Streaming](#streaming) | — | Receiver settings for ports `29765` and `29763` (PICO Connect / Streaming Assistant) |
| `EyeGain` | object | all `1` | — | Per-eye gaze and eyelid openness multipliers |
| `Stale` | object | see [Stale](#stale) | — | Expiry timers, hold vs. neutralize behavior, and VRCFT 5 neutral pupil size |
| `AllowFallback` | boolean | `false` | `true` / `false` | In `Auto` mode, switches to the other receiver if the selected source stops delivering usable data |
| `AutoSelectionWindowMs` | integer | `1500` | `100–60000` | Startup window (ms) during which `Auto` waits exclusively for Bridge before accepting Streaming |
| `LegacyExpressionCeiling` | boolean | `false` | `true` / `false` | Caps expression output at `0.99` instead of `1.0` (used for SAFT migration; not needed on fresh installs) |
| `PostProcessing` | object | only `ChannelGain` on | — | PC expression post-processing rules; see [`PostProcessing`](#postprocessing) |
| `Migration` | object or `null` | `null` | — | Provenance metadata written by `UnifiedPicoConfigTool`; ignored at runtime |

### Source Selection

| `Source` | Receivers Started | Behavior |
|---|---|---|
| `"Auto"` | Bridge + Streaming | Accepts only Bridge during the first `AutoSelectionWindowMs`; afterward locks onto whichever receiver delivers usable frames first |
| `"Bridge"` | Bridge only (`9030`) | Always uses Bridge |
| `"Streaming"` | Streaming only (`29765` / `29763`) | Always uses Streaming immediately |

A frame counts as usable when it arrives within the `Stale` window and carries data that can be written under the active eye/face toggles. Heartbeats, all-invalid packets, or merely right-sized datagrams never claim source ownership.

Once locked, the chosen source stays active for the session—even if the other source starts sending later. The module never fuses eyes from one source with mouth expressions from another. Setting `AllowFallback: true` (effective only when `Source` is `"Auto"`) allows a full switchover if the active source becomes unusable while the other is usable.

### Eye & Face Toggles

`DisableEyeTracking` and `DisableFaceTracking` intersect with what VRCFT allows the module to own. Channels not owned by this module are never overwritten (allowing mixed setups like PICO eyes + Cymple mouth). If both are `true`, no UDP sockets are opened.

With `BridgeSplit`, these toggles are sent in the `PXR_SUB` subscription request so the headset transmits only the subscribed channels. Other protocols filter locally while the sender keeps transmitting everything.

---

## `Bridge`

Handles PicoFacialBridge (`BridgeSplit`) and PicoFacialDataDaemon (`DaemonLegacy536`) on fixed UDP port `9030`.

| Key | Type | Default | Valid Values & Fallback | What It Does |
|---|---|---|---|---|
| `IP` | string or `null` | `null` | Unicast IPv4 literal (not `0.0.0.0`); invalid → `null` | Target headset IP. `null` enables LAN multicast discovery |
| `ListenAddress` | string | `"0.0.0.0"` | Non-multicast, non-broadcast IPv4; invalid → `"0.0.0.0"` | Local interface to bind |
| `Protocol` | string enum or `null` | `null` | `"BridgeSplit"`, `"DaemonLegacy536"`, or `null`; invalid → `null` | Pins a specific protocol; `null` detects from the first valid packet |
| `ReconnectMs` | integer | `2000` | `100–60000`; out-of-range → `2000` | Discovery retry interval (ms) while unconnected, and reply acceptance window (minimum 1 s) |
| `ControlTimeoutMs` | integer | `30000` | `1000–120000`; out-of-range → `30000` | Silence timeout (ms) with no control or data packets before dropping the peer and rediscovering |

- **Auto-discovery (`IP: null`):** Sends `DISCOVER_DAEMON` to `239.255.255.250:9030` across active, multicast-capable Ethernet and Wi-Fi adapters, skipping adapters whose names match VPN, virtual machine, or tunnel keywords (`virtual`, `vpn`, `tunnel`, `wireguard`, `zerotier`, `tailscale`, `tap-`, `hyper-v`, `vmware`, `virtualbox`). Replies are accepted only from the same subnet as a discovery adapter (excluding local PC addresses).
- **Pinned IP (`IP` set):** Sends discovery via unicast directly to that address, accepts packets only from that IP on port `9030`, and bypasses the subnet check.
- **Bind restriction (`ListenAddress`):** When set to a specific local IPv4 address instead of `0.0.0.0`, discovery also runs only on that adapter.
- **Timeouts:** If frames stop for 250 ms while control heartbeats continue, the log state changes to `MissingFrames` without dropping the peer. After `ControlTimeoutMs` with no control or data activity, the module drops the peer (without sending `STOP`) and restarts discovery. If a `BridgeSplit` mode announcement (`PXR_MODE`) is not refreshed within `min(ControlTimeoutMs, 20000)` ms, confirmed enhanced capabilities are withdrawn.

---

## `Streaming`

Handles local streaming senders on fixed UDP ports `29765` (`StreamingHeader16`, e.g., PICO Connect) and `29763` (`StreamingBodyOnly`, headerless 892-byte format).

| Key | Type | Default | Valid Values & Fallback | What It Does |
|---|---|---|---|---|
| `ListenAddress` | string | `"0.0.0.0"` | Same as `Bridge.ListenAddress` | Local interface to bind |
| `LocalOnly` | boolean | `true` | `true` / `false` | Accepts packets only from loopback or active local PC IPv4 addresses |
| `IP` | string or `null` | `null` | Unicast IPv4 literal (not `0.0.0.0`); invalid → `null` | Accepts packets only from this sender address |
| `Protocol` | string enum or `null` | `null` | `"StreamingHeader16"`, `"StreamingBodyOnly"`, or `null` | Pins the streaming format and controls which ports are opened |
| `SessionTimeoutMs` | integer | `2000` | `100–60000`; out-of-range → `2000` | Silence timeout (ms) before ending the session and releasing sender/protocol locks |
| `HeaderProfile` | object or `null` | `null` | See [`HeaderProfile`](#headerprofile) | Optional strict 16-byte header matching for port `29765` |

| `Streaming.Protocol` | Ports Opened | Packet Acceptance |
|---|---|---|
| `null` | `29765` and `29763` | First valid frame locks the protocol until `SessionTimeoutMs` expires |
| `"StreamingHeader16"` | `29765` | Datagrams `>= 908` bytes with header `trackingType == 2` (reads 892 bytes after the 16-byte header, ignores trailing bytes; PICO Connect 10.6.6 sends 912 bytes) |
| `"StreamingBodyOnly"` | `29763` | Datagrams of exactly `892` bytes |

If `LocalOnly` is `true` and `Streaming.IP` is set to a non-local address, the streaming receiver fails to start and logs an error.

### `HeaderProfile`

By default (`null`), port `29765` checks only length (`>= 908` bytes) and `trackingType == 2`, matching SAFT and pike. Supply all six fields to enforce strict header checks (no built-in magic defaults exist, and fragmented packets are not reassembled):

| Key | Type | Range |
|---|---|---|
| `StartCode1` | integer | `0–255` |
| `StartCode2` | integer | `0–255` |
| `SubType` | integer | `0–255` |
| `MultiPack` | integer | `0–255` |
| `CurrentIndex` | integer | `0–255` |
| `Version` | integer | `0–65535` |

If any of the six fields is missing or out of range, the entire `HeaderProfile` falls back to `null` (default permissive acceptance on `29765`; port `29763` is unaffected). See the [Protocol Reference](protocol.md) for byte offsets.

---

## `EyeGain` & Eye Resolution

Across every sender, gaze and eyelid openness come from the expression model, while native eye packets supply only the measured pupil diameter:

- **Gaze (`blendshape-derived`):** Left X = `EyeLookInL - EyeLookOutL`, Right X = `EyeLookOutR - EyeLookInR`, Y (both eyes) = `EyeLookUp - EyeLookDown`. Requires all four `EyeLook` shapes for that eye; carries no vergence data.
- **Eyelid openness (`blendshape-blink`):** `1 - EyeBlink` per eye. VRCFT derives closure as `1 - Openness`.
- **Measured pupil:** Valid native measurements in `1–12` mm are used and held on invalid frames by default. When unavailable, VRCFT 5 receives `Stale.NeutralPupilMillimeters` (`3.5` mm) and VRCFT 6 receives `0`.
- **Independent `X` packets:** Each eye carries a quality state (`1` usable, `-1` unknown/usable, `0` unusable). When an eye reports `0`, only that eye holds its own previous independent value (or stays neutral if no valid independent sample has arrived yet).

| Key | Type | Default | Valid Range | What It Does |
|---|---|---|---|---|
| `LeftX`, `LeftY`, `RightX`, `RightY` | number | `1` | `0–10` | Multiplies each eye's gaze axes before clamping to `[-1, 1]` |
| `LeftOpenness`, `RightOpenness` | number | `1` | `0–10` | Multiplies each eye's openness before clamping to `[0, 1]` |

Out-of-range or non-finite values fall back to `1`. Gains do not affect pupil diameter and stack with VRCFT's own adjustments. Keep `LeftOpenness` and `RightOpenness` at `1` when testing blink sensitivity, as values above `1` clip slight eyelid closures back to fully open.

---

## `Stale`

| Key | Type | Default | Valid Range | What It Does |
|---|---|---|---|---|
| `NeutralizeOnTimeout` | boolean | `false` | `true` / `false` | `false`: retain last gaze, eyelids, pupil, and expressions on expiry. `true`: return to neutral on expiry |
| `EyeMs` | integer | `250` | `1–5000` | Freshness limit (ms) for measured pupil data from native eye packets |
| `FaceMs` | integer | `250` | `1–5000` | Freshness limit (ms) for expression data (and expression-derived gaze/openness) |
| `NeutralPupilMillimeters` | number | `3.5` | `1–10` | Fallback pupil diameter (mm) on VRCFT 5 when no measurement is available (VRCFT 6 always gets `0`) |

All ages are measured using the PC's local monotonic arrival clock, never sender timestamps.

Even when `NeutralizeOnTimeout` is `false`, freshness limits still govern `Auto` source selection and reset post-processing temporal state (`CheekAsymmetry`, `TongueHold`). A new session, source/mode switch, module deactivation, or teardown always resets output. The `MissingFrames` log status always triggers after 250 ms regardless of `Stale` settings.

---

## `PostProcessing`

Contains 14 rules that run in a fixed order. Omitted rules keep their defaults; omitted parameters inside a rule keep their defaults.

Only `ChannelGain` is enabled by default:

| Key | Type | Default | Range | What It Does |
|---|---|---|---|---|
| `Enabled` | boolean | `true` | `true` / `false` | Enables or disables per-channel gain |
| `DefaultGain` | number | `1` | `0–10` | Multiplier for channels not listed in `Gains` |
| `Gains` | object | `EyeWideLeft/Right: 2`; `EyeSquintLeft/Right`, `JawOpen: 1` | `0–10` per channel | Per-channel gain map (channel names are case-insensitive) |

Writing `Gains` in your config replaces the entire default dictionary—channels you omit (including `EyeWideLeft/Right`) fall back to `DefaultGain`. An unknown channel name, case-insensitive duplicate, or out-of-range value disables `ChannelGain` completely.

See the [Post-Processing Reference](postprocessing.md) for all 14 rules, formulas, and valid channel names.

---

## `Migration`

Written automatically by `UnifiedPicoConfigTool import` to document provenance; not read at runtime. Leave as `null` when writing configs by hand.

| Key | Type | Description |
|---|---|---|
| `SourceFile` | string | Full path of the imported legacy file |
| `SourceVersion` | string | Explicit legacy source label |
| `ImportedAtUtc` | string | Import timestamp (UTC ISO 8601) |
| `Differences` | string[] | Same entries as the generated migration report |

> [!WARNING]
> `SourceFile` and `Differences` can contain your Windows username and local IP addresses. Review or set `"Migration": null` before sharing a config file.

---

## Validation & Error Handling

Fields are validated independently—one bad value will not discard the rest of your file:

| Condition | Result |
|---|---|
| Config file missing | Writes a default `UnifiedPicoModule.json` (or uses in-memory defaults if write fails) |
| Unreadable file, malformed JSON, or root is not an object | Uses full defaults in memory; file on disk is untouched |
| `ConfigVersion` missing, duplicated, or not `1` | Uses full defaults in memory |
| Unknown field or duplicate key in same object | Ignored (keeps default) and logged |
| Wrong type (including numeric enum or `null` for an object) | Ignored (keeps default) and logged |
| Number out of range or invalid IPv4 address | Falls back to that field's default and logs |
| Incomplete or invalid `Streaming.HeaderProfile` | Entire `HeaderProfile` treated as `null` |
| Duplicate rule or invalid/unknown/out-of-range parameter inside a rule | Disables **only that rule**; other rules keep running |
| `PostProcessing` is not an object | Disables all rules (including `ChannelGain`) |

IP fields accept IPv4 literals only (no hostnames or IPv6). Multicast (`>= 224.0.0.0`) and `255.255.255.255` are rejected; `0.0.0.0` is allowed only for `ListenAddress`.

---

## Legacy Config Migration Summary

`UnifiedPicoConfigTool import` converts four pinned legacy formats into `UnifiedPicoModule.json` plus a difference report:

| Source Label | Legacy File | Key Resulting Settings |
|---|---|---|
| `bridge-current-0.1-beta-split-fix1` | `PicoFacialDataModule.json` (split fork) | `Source: "Bridge"`, `Bridge.Protocol: "BridgeSplit"`, `ShapeGain` → `ChannelGain` |
| `bridge-upstream-0ab66348` | `PicoFacialDataModule.json` (upstream) | `Source: "Bridge"`, `Bridge.Protocol: "DaemonLegacy536"` |
| `saft-ab500f03` | `PicoModuleConfig.json` (SAFT) | `Source: "Streaming"`, `Streaming.Protocol: "StreamingHeader16"`, `LegacyExpressionCeiling: true`, `scales` → `ChannelGain` + `EyeGain` |
| `pike-c026aa4` | `config.ini` (pike branch) | `Source: "Auto"`, `eye_gain` → `EyeGain`, `mode` expanded into explicit post-processing rules |

Notable differences from a fresh install:

- Migration does **not** add the fresh-install `EyeWide` 2x gain; `ChannelGain` is enabled only when the legacy config had valid, enabled gain settings.
- Legacy gaze/openness source selectors, `TrackingSettings` pupil thresholds, and `BridgeMode` are not imported and are listed in the report.
- **pike `mode` expansion** (expanded once at import time; there are no runtime presets):
  - `mode 0`: all post-processing rules off.
  - `mode 1`: enables `ChannelGain`, `FrownJawAttenuation`, `UpperUpPressGate`, `PuckerFunnelAssist`, `SmileRollSuppression`, `TongueJawGate` (all gating rules with `TransitionWidth: 0`), and `Symmetry` (`["Smile"]`, `Max`). Gains: `JawOpen = 1/0.75`; `JawLeft/Right`, `MouthUpperLeft/Right`, `MouthLowerLeft/Right`, `MouthCornerPullLeft/Right`, `MouthCornerSlantLeft/Right = 2`; `MouthUpperUpLeft/Right = 1.25`.
  - `mode 2` *(default if `mode` is omitted)*: everything in `mode 1`, plus `Symmetry` expanded to `["Smile", "Frown", "Stretch", "UpperUp"]`, `BrowInnerSeparation` (`Strength: 1`), `ShrugToFrown` (`Range: 0.05`, `TransitionWidth: 0`), and extra gains (`MouthClosed = 1/0.65`, `MouthStretchLeft/Right = 1/0.3`, `BrowInnerUpLeft/Right` & `BrowOuterUpLeft/Right = 2`). An invalid or duplicate `mode` leaves all rules disabled.

For CLI syntax, exit codes, and detailed field mappings, see the [ConfigTool Guide](../tools/ConfigTool/README.md).
