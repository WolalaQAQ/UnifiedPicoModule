# UnifiedPicoConfigTool

**English** | [简体中文](README_zh.md) · [Back to README](../../README.md)

`UnifiedPicoConfigTool` is an offline CLI helper for UnifiedPicoModule. It validates `UnifiedPicoModule.json`, exports a clean default config, and migrates configurations from older PICO VRCFT modules.

It does **not** require VRCFaceTracking or the VRCFT SDK, never modifies legacy files, never overwrites existing output paths, and never installs configs automatically—you review the output and copy it into place manually.

## Build & Quick Start

Requires the **.NET 10 SDK**. Run from the repository root:

```powershell
dotnet build tools/ConfigTool/ConfigTool.csproj -c Release
$tool = "tools/ConfigTool/bin/Release/net10.0/UnifiedPicoConfigTool.dll"

# Show usage and supported legacy source labels
dotnet $tool --help

# 1. Validate an existing v1 config (read-only)
dotnet $tool validate UnifiedPicoModule.json

# 2. Export a clean default config for comparison
dotnet $tool defaults UnifiedPicoModule.defaults.json

# 3. Migrate a legacy config into a new v1 config + difference report
dotnet $tool import legacy/PicoFacialDataModule.json bridge-upstream-0ab66348 UnifiedPicoModule.imported.json migration-report.txt

# 4. Validate the migrated config
dotnet $tool validate UnifiedPicoModule.imported.json
```

Building also produces `UnifiedPicoConfigTool.exe` in the same folder. If you already prepared the pinned VRCFT SDK source, `./tools/build.ps1 -Configuration Release` builds both the module and this tool together (see [Development Guide](../../docs/development.md)).

### Applying a Migrated Config

1. Read `migration-report.txt` and review any unimported fields or behavior differences.
2. Fully exit VRCFT and back up your current `UnifiedPicoModule.json`.
3. Copy your reviewed config to:
   ```text
   %APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442\UnifiedPicoModule.json
   ```
4. Relaunch VRCFT and check the **Output** tab for any config warnings.

---

## Commands & Exit Codes

| Command | Arguments | What It Does |
|---|---|---|
| `validate` | `<config.json>` | Read-only check using the exact runtime validation rules (`ConfigStore.ReadOnly`); never creates or rewrites the file |
| `defaults` | `<output.json>` | Writes model defaults (`ConfigVersion: 1`, only `ChannelGain` EyeWide 2x enabled) to a new file |
| `import` | `<legacy-file> <source-version> <new-config.json> <report.txt>` | Converts one legacy config into a new v1 JSON config plus a UTF-8 migration report |
| `--help` / `-h` | *(none)* | Prints syntax and supported `<source-version>` labels |

| Exit Code | `validate` | `defaults` | `import` |
|---|---|---|---|
| `0` | Clean (no diagnostics needing review) | Default file written | Both config and report written (differences listed in the report do not change the exit code) |
| `1` | Missing/unreadable file, invalid JSON, non-object root, or bad args | Output already exists, missing parent dir, write error, or bad args | Duplicate/occupied paths, unknown source version, unreadable/invalid legacy file, write error, or bad args |
| `2` | Diagnostics needing manual review (fallbacks, disabled rules, unused fields) | — | — |

### Command Notes

- **`validate`:** Output lines starting with `启用 ` list active post-processing rules and are purely informational (they do not trigger exit code `2`). Unlike the runtime module, malformed JSON or a non-object root returns exit code `1` rather than falling back silently.
- **`defaults`:** Parent directory must already exist; fails with exit code `1` if the target file exists.
- **`import`:** The three file paths must be distinct (case-insensitive), and neither output path may exist. The tool writes the report first, writes the new JSON config second, and appends `新配置成功写入：<path>` to the end of the report upon completion. If an error occurs midway, inspect the target paths and retry with unused output paths.

---

## Supported Source Versions

You must specify `<source-version>` explicitly (case-sensitive). Because upstream and split-fork `PicoFacialDataModule.json` share the same filename with different field semantics, the tool never guesses from filenames.

| Source Version Label | Legacy File & Pinned Reference | Format |
|---|---|---|
| `bridge-current-0.1-beta-split-fix1` | Split-fork `PicoFacialDataModule.json` (`0.1-beta-split-fix1`) | JSON |
| `bridge-upstream-0ab66348` | Upstream `PicoFacialDataModule.json` ([`0ab66348`](https://github.com/thoricelli/PicoFacialDataModule/tree/0ab66348bf970763322fe261b54445bcc5a2b23e)) | JSON |
| `saft-ab500f03` | SAFT `PicoModuleConfig.json` ([`ab500f03`](https://github.com/regzo2/PicoStreamingAssistantFTUDP/tree/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7)) | JSON |
| `pike-c026aa4` | pike branch `config.ini` ([`c026aa4`](https://github.com/pikepikeid/VRCFTPicoModule/tree/c026aa455da60dd50492b3d36c9d360d0c343b8e)) | `key: value` text |

---

## Field Mapping Summary

Across all four sources:
- `ChannelGain` starts **disabled** (migration never adds the fresh-install EyeWide 2x boost) and is enabled only if the legacy file contains valid, enabled gain settings.
- Gaze/openness source selectors are not imported; gaze is always expression-derived and openness is `1 - EyeBlink`.
- Duplicate keys, unknown keys, and gains outside `0–10` are skipped and logged in the report. The final config runs through `ConfigValidator` before writing.

### PicoFacialDataModule (`bridge-current-*` / `bridge-upstream-*`)

- Fixed settings: `Source: "Bridge"`, `LegacyExpressionCeiling: false`, `Bridge.Protocol`: `"BridgeSplit"` (`bridge-current`) or `"DaemonLegacy536"` (`bridge-upstream`).
- Imported: `DisableEyeTracking`, `DisableFaceTracking`, `IP` → `Bridge.IP` (invalid IPv4 falls back to auto-discovery).
- `ShapeGain` (`bridge-current` only) → `PostProcessing.ChannelGain` (`Enabled`, `DefaultGain`, `Gains`; omitting `Gains` fills all mapped channels with `1`, while `{}` keeps an empty object so `DefaultGain` applies). Any invalid entry disables `ChannelGain`.
- Not imported (noted in report): `ShapeGain` under `bridge-upstream`, `TrackingSettings.eyePupilDilationEyeOpennessThreshold`, `BridgeMode`.

### SAFT (`saft-ab500f03`)

- Fixed settings: `Source: "Streaming"`, `Streaming.Protocol: "StreamingHeader16"`, `Streaming.HeaderProfile: null`, `LegacyExpressionCeiling: true`.
- `scales.EyeXGazeLeft/Right`, `EyeYGazeLeft/Right`, `EyeOpennessLeft/Right` → `EyeGain` (fixes legacy SAFT's bug that reused `EyeXGazeLeft` for right-eye Y by reading `EyeYGazeRight` properly).
- Remaining valid expression keys in `scales` → `ChannelGain.Gains` (`DefaultGain: 1`). Any unknown/duplicate/invalid expression key disables `ChannelGain` as a whole; invalid eye keys reset only that `EyeGain` entry to `1`.

### pike (`pike-c026aa4`)

Parses `key: value` lines (case-insensitive keys; blank lines and `#` comments ignored; lines without `:` reported).

- Fixed setting: `Source: "Auto"`.
- `eye-tracking` / `expression-tracking` (`enable` / `disable`) → `DisableEyeTracking` / `DisableFaceTracking`.
- `eye_gain: X,Y` → `EyeGain.LeftX/RightX = X`, `LeftY/RightY = Y` (invalid format keeps `1`).
- `mode` (`0`, `1`, or `2`; defaults to `2` if omitted; invalid/duplicate disables all rules) → expanded once into explicit `PostProcessing` rules (no runtime presets). Exact per-mode expansions are listed under [Legacy Config Migration Summary](../../docs/configuration.md#legacy-config-migration-summary).

---

## Migration Report & Privacy

The migration report (currently written in Chinese) is saved as a standalone UTF-8 `.txt` file and mirrored into `Migration.Differences` inside the generated JSON. It documents the source path, label, UTC timestamp, unimported/unknown/duplicate fields, behavior differences, and post-validation fixes.

> [!WARNING]
> Both the report and the `Migration` object include the absolute path of your legacy file (often containing your Windows username), legacy IP addresses, and unknown field values verbatim. Review and redact both files—or set `"Migration": null`—before sharing.

See also: [Configuration Reference](../../docs/configuration.md) · [Post-Processing Reference](../../docs/postprocessing.md) · [Privacy & Network Security](../../docs/privacy.md) · [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md).
