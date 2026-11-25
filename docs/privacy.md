# Privacy & Network Security

**English** | [简体中文](privacy_zh.md) · [Back to README](../README.md)

This page documents what data UnifiedPicoModule touches, what it writes locally, what appears in VRCFT logs, and how it handles LAN UDP traffic.

## Data Handled

UnifiedPicoModule receives gaze, eye openness, pupil diameter (when provided), and facial expression weights from your local sender, processes them **strictly in memory**, and writes the result into VRCFT's `UnifiedTracking.Data`. Downstream OSC forwarding to VRChat is handled by VRCFT.

The module does **not**:
- save raw tracking frames, decoded values, or packet captures to disk;
- connect to the internet (no telemetry, update checks, or downloads; the module icon is embedded inside the DLL);
- collect user accounts, hardware IDs, or headset serial numbers.

Each `BridgeSplit` subscription session generates a fresh random 16-character hexadecimal ID (`Guid.NewGuid().ToString("N")[..16]`) unrelated to your device or account.

## Local Files

At runtime, the module writes to disk in **one situation only**: creating a default `UnifiedPicoModule.json` if none exists in the module directory. Existing configs are read-only and never overwritten.

Files that may contain local environment info:
- **`UnifiedPicoModule.json`:** user-entered `Bridge.IP`, `Streaming.IP`, `ListenAddress`, and any `Migration` block created by `UnifiedPicoConfigTool`.
- **`UnifiedPicoConfigTool import` outputs:** writes only to explicit CLI paths (never overwrites). The generated JSON and report record the full path of the legacy config (which may contain your Windows username), output path, UTC timestamp, legacy IP addresses, and unrecognized legacy fields verbatim.

## Log Contents

Logs go through VRCFT's logging delegate; the module does not create its own log files.

Logs **never** print gaze, openness, pupil, or expression numbers, but they can include:
- the full path to `UnifiedPicoModule.json` under `%APPDATA%` (includes your Windows username);
- unrecognized or invalid config fields and the active parameters of enabled post-processing rules;
- bound listen addresses/ports, locked peer IP:port (e.g., `Bridge peer locked: 192.168.1.20:9030`), and destination addresses of failed UDP sends;
- a 5-second status summary (state, session ID, receive Hz, frame ages, validity flags, announced capabilities, dropped packet count, gaze/openness/pupil origins, and internal processing timings);
- exception types and messages.

Each warning category is rate-limited to once every 5 seconds, and each source bounds its async log queue to 32 messages—preventing malformed traffic from flooding VRCFT's log.

## Network Traffic & Ports

| UDP Port | Sender | Direction & Control Traffic |
|---|---|---|
| `9030` | PicoFacialBridge / PicoFacialDataDaemon | Receives tracking frames; sends `DISCOVER_DAEMON` (multicast `239.255.255.250:9030` with TTL=1 on active non-VPN Ethernet/Wi-Fi adapters, or unicast when `Bridge.IP` is set), `POLO` heartbeat replies, `PXR_SUB` channel subscriptions, and `STOP` on teardown |
| `29765` | PICO Connect (`StreamingHeader16`) | Receives tracking frames; replies `POLO` to `MARCO\0` heartbeats |
| `29763` | Headerless streaming (`StreamingBodyOnly`) | Receives tracking frames; replies `POLO` to `MARCO\0` heartbeats |

Ports are bound exclusively (`ExclusiveAddressUse = true`) on `0.0.0.0` by default, or restricted to a specific interface via `ListenAddress`. Setting `Source` to `"Bridge"` skips opening `29765`/`29763`; setting `"Streaming"` skips opening `9030`.

## Source Validation

UDP carries no encryption or cryptographic authentication, and source IPs can be spoofed on a shared LAN. The module enforces structural and endpoint checks to reject accidental or malformed traffic (not to defend against a hostile LAN):

- **All receivers:** IPv4 unicast source only; strict per-format length checks (see [Protocol Reference](protocol.md)); datagrams over `2048` bytes dropped; first packet of a session must contain valid fields; duplicate or backward timestamps dropped; protocol locked for the session.
- **Bridge (`9030`):** Source port must be `9030`. Accepts only `Bridge.IP` when configured, or same-subnet peers (excluding local PC addresses) within the discovery reply window (`max(1s, ReconnectMs)`). Once locked, only the locked IP:port is accepted. `PXR_MODE` must be printable ASCII with the exact 5 defined keys; `PXR_SUB_ACK` must match the session's random ID, non-exceeding mask, and non-regressing epoch; `BridgeSplit` data must match the acknowledged epoch and mask. Lock clears after `ControlTimeoutMs` (default 30 s) of silence.
- **Streaming (`29765` / `29763`):** `Streaming.LocalOnly` defaults to `true` (loopback or active local PC IPv4 only; non-local `Streaming.IP` fails startup). Locks sender IP, port, and protocol on the first valid frame and clears after `SessionTimeoutMs` (default 2 s). Strict 16-byte header matching applies when `Streaming.HeaderProfile` is configured.

## Practical Recommendations

- Run UnifiedPicoModule only on trusted local networks (avoid public Wi-Fi).
- Never forward UDP ports `9030`, `29765`, or `29763` on your router.
- In Windows Firewall, permit VRCFT on **Private** networks only.
- Keep `Streaming.LocalOnly: true` for local PC streamers like PICO Connect.
- Set `Bridge.IP` if multiple headsets share your LAN, and set `Source` to `"Bridge"` or `"Streaming"` if you only use one sender type.

## Before Sharing Logs or Configs

- Redact local IP addresses and paths containing your Windows username.
- Trim `Migration` entries (or set `"Migration": null`) and share only the config fields relevant to your issue.
- Do not upload raw packet captures or tracking recordings.

## Release Package Hygiene

Official release ZIPs ship with a freshly generated default `UnifiedPicoModule.json` (no personal IPs, migration metadata, or local paths). Builds apply `<PathMap>` in `Directory.Build.props` so the build machine's user directory is not embedded in debug paths, and `sdk-provenance.json` records only the pinned SDK commit, version, and file digests.
