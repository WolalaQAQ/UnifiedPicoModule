# Development guide

English | [简体中文](development_zh.md) · [README](../README.md)

This document describes how UnifiedPicoModule is built, what the package contains, how releases are published, and how the code is organized.

## Requirements

- Windows and PowerShell
- .NET 10 SDK
- Git
- The pinned version of the VRCFaceTracking source

The build does not depend on an Android toolchain or any other PICO project. The build and packaging scripts do not install the module, do not copy files into VRCFT's `CustomLibs` directory, and do not change any device or streaming software settings.

## Building

### Preparing the SDK source

The module is compiled against VRCFaceTracking commit `7add14873e398c4095d06913cdeee7a01fc43fab`, which corresponds to assembly version `5.4.5.0` and target framework `net10.0`. In the repository root, run:

```powershell
git clone https://github.com/benaclejames/VRCFaceTracking.git .sdk/source/VRCFaceTracking
git -C .sdk/source/VRCFaceTracking switch --detach 7add14873e398c4095d06913cdeee7a01fc43fab
```

`.sdk/` is a local cache directory and is excluded by `.gitignore`.

### Running the build script

```powershell
./tools/build.ps1 -Configuration Release
```

`-Configuration` accepts `Debug` or `Release` and defaults to `Release`. The SDK source directory is determined in the following order:

1. the `-SdkSource` parameter;
2. the `VRCFT_SDK_SOURCE` environment variable;
3. `.sdk/source/VRCFaceTracking` in the repository.

The script never downloads the source. If the source directory cannot be found, it exits with an error.

### Build steps

`tools/build.ps1` performs the following steps in order. A failure at any step stops the build.

1. Verify that `HEAD` of the source directory is the pinned commit.
2. Verify that the source directory has no modifications to tracked files. The script does not discard or revert such modifications.
3. Compute SHA256 fingerprints of four key files and compare them with the values recorded in the script: `Directory.Build.props`, `Directory.Packages.props`, `VRCFaceTracking.SDK/ExtTrackingModule.cs` and `VRCFaceTracking.Core/Params/Expressions/UnifiedExpressions.cs`. Before hashing, the UTF-8 BOM is removed and CRLF is normalized to LF, so differing Git line-ending settings do not cause false mismatches.
4. Build `VRCFaceTracking.Core` in the Release configuration. All intermediate files and output are written to `.sdk/build`, not to the SDK source directory.
5. Verify that the assembly version of `VRCFaceTracking.Core.dll` and `VRCFaceTracking.SDK.dll` is `5.4.5.0`, then copy both to `.sdk/`.
6. Write `.sdk/sdk-provenance.json`, which records the commit, assembly version, target framework, fingerprint normalization and the fingerprints of the four files.
7. Build the module project `UnifiedPicoModule.csproj`.
8. Build the configuration tool `tools/ConfigTool/ConfigTool.csproj`.

Changing the SDK version requires updating the commit and fingerprints in the script, `VRCFT_COMMIT` in `release.yml` and the version check in `UnifiedPicoModule.csproj` together, and should be handled as a separate compatibility change.

### Build output

| Output | Path |
|---|---|
| Module | `bin/<Configuration>/net10.0/UnifiedPicoModule.dll` |
| Configuration tool | `tools/ConfigTool/bin/<Configuration>/net10.0/UnifiedPicoConfigTool.dll` |
| SDK assemblies | `.sdk/VRCFaceTracking.Core.dll`, `.sdk/VRCFaceTracking.SDK.dll` |
| SDK provenance record | `.sdk/sdk-provenance.json` |

## Project files

### UnifiedPicoModule.csproj

- Targets `net10.0`, with nullable reference types and implicit usings enabled, and all warnings treated as errors.
- The version is set by the `Version` property and is currently `0.4.0`.
- References `VRCFaceTracking.SDK.dll` and `VRCFaceTracking.Core.dll` under `$(VrcftSdkDir)` through `HintPath`, with `Private=false`, so neither DLL is copied to the output directory. `VrcftSdkDir` defaults to `.sdk` in the repository and can be overridden as an MSBuild property.
- The `CheckSdk` target checks that both DLLs exist before references are resolved and requires their assembly version to be `5.4.5.0`.
- References `Microsoft.Extensions.Logging.Abstractions` 10.0.1 with `PrivateAssets="all"`.
- Excludes source files under `tools/`, `.sdk/`, `artifacts/`, `docs/` and `reference/` from compilation.
- Embeds `assets/branding/module-icon.png` as a resource with the logical name `UnifiedPicoModule.Branding.ModuleIcon.png`. It is used as the icon in the module list.

### Directory.Build.props

Uses `PathMap` to map the project directory to `/_/UnifiedPicoModule/`, so source paths recorded in the build output do not include the build machine's local directories. The setting applies to both the module and the configuration tool.

### tools/ConfigTool/ConfigTool.csproj

The configuration tool is a separate console application with the assembly name `UnifiedPicoConfigTool`, targeting `net10.0`. Default compile items are disabled; the project compiles only `Program.cs` and links in `Core/Contracts.cs` and `Configuration/*.cs`. The tool does not reference the VRCFT SDK, and it shares the same source for reading, validating and migrating configuration as the module.

The tool provides the `validate`, `defaults` and `import` commands. See the [configuration tool guide](../tools/ConfigTool/README.md) for usage.

### module.json

The module metadata read by VRCFT contains the following fields:

| Field | Current value or description |
|---|---|
| `ModuleId` | `8322e16d-c38b-42f0-8d35-68f27227b442`; also determines the module's directory name under `CustomLibs` |
| `Version` | `0.4.0`; must match the version in `UnifiedPicoModule.csproj` |
| `ModuleName` | `Unified PICO Face Tracking` |
| `ModuleDescription` / `UsageInstructions` | Text shown in the module list |
| `DownloadUrl` | Currently empty |
| `ModulePageUrl` | The project's GitHub repository URL |
| `DllFileName` | `UnifiedPicoModule.dll` |

## Packaging

```powershell
./tools/package.ps1
```

By default the script first runs `tools/build.ps1 -Configuration Release`, and it accepts the same `-SdkSource` parameter and `VRCFT_SDK_SOURCE` environment variable. With `-SkipBuild`, the build is skipped and the existing Release output is used directly; in that case there is no guarantee that the output matches the current source.

### ModulePageUrl check

Before packaging, the script requires `ModulePageUrl` in `module.json` to be a non-empty absolute URI with the `http`, `https` or `file` scheme, and refuses to package otherwise. VRCFT 5.4.5 binds this field directly to `HyperlinkButton.NavigateUri`, and during testing on a real headset both an empty string and `null` caused the Module Registry page to crash. A placeholder is acceptable for local experimental builds, but a public release should use the project's actual page.

### Package contents

The package has a flat layout in which the root is the module directory. It contains:

- `UnifiedPicoModule.dll`
- `module.json`
- `UnifiedPicoModule.json`: the default configuration exported from the configuration model by the configuration tool's `defaults` command
- `config/UnifiedPicoModule.example.json`: identical to the default configuration above
- `sdk-provenance.json`
- `README.md`, `README_zh.md`, `CHANGELOG.md`, `CHANGELOG_zh.md`, `CONTRIBUTING.md`, `CONTRIBUTING_zh.md`, `THIRD_PARTY_NOTICES.md`, `THIRD_PARTY_NOTICES_zh.md`, `LICENSE`
- the configuration reference, post-processing reference, protocol reference, development guide, and privacy and network security documents under `docs/`, each in English and Chinese
- `tools/ConfigTool/README.md`, `tools/ConfigTool/README_zh.md`
- the `logo-dark`, `logo-light` and `module-icon` images under `assets/branding/`, each as SVG and PNG

Every file is copied individually from an explicit list; the `docs/` directory is never copied recursively. The default configuration is always exported afresh, and the developer's local `UnifiedPicoModule.json` is never read. The package does not contain VRCFaceTracking DLLs, SDK source, the configuration tool binary or PDB files.

### Output files

Each packaging run creates a timestamped staging directory `stage-<yyyyMMdd-HHmmss-fff>` under `artifacts/`, together with:

- `UnifiedPicoModule-<Version>-<yyyyMMdd-HHmmss-fff>.zip`
- a `.zip.sha256` file with the same name, containing `<SHA256>  <file name>`

The timestamp in the file names keeps each run from overwriting earlier output. The script does not clean up previous output; old files under `artifacts/` can be deleted manually when needed.

## Releasing

`.github/workflows/release.yml` runs when a tag starting with `v` is pushed to GitHub. Ordinary branch pushes and pull requests do not trigger it.

### Preparing a release

1. Set `Version` in `module.json` and `UnifiedPicoModule.csproj` to the same version number, without the `v` prefix.
2. The startup log message in `Sdk/UnifiedPicoTrackingModule.cs` contains the version number as a literal and must be updated as well.
3. Set `ModulePageUrl` in `module.json` to the project's actual page.
4. Commit and push these changes, then create and push a tag for that commit:

```powershell
git tag -a v0.4.0 -m "UnifiedPicoModule 0.4.0"
git push origin v0.4.0
```

The tag format is `vMAJOR.MINOR.PATCH`, optionally followed by a SemVer pre-release suffix such as `v0.4.0-rc.1`. Build metadata (`+build`) is not accepted, and leading zeros are not allowed in the numeric parts or in numeric pre-release identifiers.

### Build job

The build job runs on `windows-latest` with `contents: read` permission and performs the following steps:

1. Check out the repository without persisting credentials.
2. Validate the tag format and verify that the tag version exactly matches the versions in `module.json` and `UnifiedPicoModule.csproj`.
3. Install the .NET 10 SDK.
4. Check out the pinned VRCFaceTracking commit into `.sdk/source/VRCFaceTracking`. The commit is recorded in the workflow's `VRCFT_COMMIT` environment variable and is verified again independently by `tools/build.ps1`.
5. Run `tools/package.ps1 -SdkSource .sdk/source/VRCFaceTracking`.
6. Inspect the resulting ZIP. Entry paths must not contain `agents`, `archive`, `private`, `.sdk`, `bin` or `obj` directories or names containing `migration` or `current-settings`; the package must not contain `.log`, `.pcap`, `.pcapng`, `.har` or `.pdb` files, or any DLL other than `UnifiedPicoModule.dll`. In the default configuration inside the package, `Bridge.IP`, `Streaming.IP` and `Migration` must be empty, and no rule under `PostProcessing` other than `ChannelGain` may have `Enabled` set to `true`.
7. Copy the ZIP to `UnifiedPicoModule-<version>.zip` without the timestamp, recompute the SHA256 and write a `.zip.sha256` file.
8. Upload both files as a workflow artifact, retained for 14 days.

### Publish job

After the build job succeeds, the publish job runs on `ubuntu-latest` with `contents: write` permission, using the `GITHUB_TOKEN` provided automatically by GitHub. It downloads both files and verifies them with `sha256sum --check --strict`, then creates the release as a draft, attaches the files, generates release notes automatically, and finally makes the draft public. If the tag has a pre-release suffix, the release is marked as a prerelease.

Note the following when using this workflow:

- If a release with the same name already exists, the creation step fails; existing releases are never overwritten.
- If the upload completes but the publish step fails, an unpublished draft is left behind and must be handled manually.
- Tags pushed with `GITHUB_TOKEN` do not trigger workflows. This is GitHub's mechanism for preventing recursive runs.
- All Actions used in the workflow are pinned to full commit SHAs.

## Code structure

```
Core/            Shared types and data contracts
Protocol/        Wire format decoding and control messages
Transport/       UDP reception, peer locking and source validation
Mapping/         Expression mapping and gaze resolution
Processing/      Expression post-processing
Configuration/   Configuration model, loading, validation and migration
Runtime/         Source selection, processing pipeline and diagnostics
Sdk/             VRCFT module entry point
tools/           Build and packaging scripts, configuration tool
config/          Default configuration example
assets/branding/ Icons and logos
docs/            Public documentation
```

`reference/` is a read-only snapshot of third-party source. It is excluded by `.gitignore`, is not compiled and is not packaged.

### Responsibilities

| Directory | Responsibility |
|---|---|
| `Core` | Enumerations (sources, protocols, connection states, PICO expressions, module output channels, etc.), the monotonic clock, raw eye and face data, source snapshots and the module output model. Source snapshots are immutable |
| `Protocol` | Decoders for the four wire formats, `BridgeSplitDecoder`, `DaemonLegacyDecoder`, `Header16Decoder` and `BodyOnlyDecoder`, plus construction and parsing of Bridge control messages (`BridgeControl`) |
| `Transport` | `UdpTrackingSource` handles socket binding, the background receive thread, timestamp deduplication, dropped packet counting and log rate limiting. `BridgeUdpSource` implements discovery, heartbeat, channel subscription and peer locking on port 9030. `StreamingUdpSource` implements reception on ports 29765 and 29763 and the local-source restriction |
| `Mapping` | `NeutralFaceMapper` maps PICO expressions to module output channels. `EyeResolver` derives each eye's gaze from the `EyeLook` shapes and its openness from `1 - EyeBlink`, and resolves the measured pupil diameter |
| `Processing` | `PostProcessor` runs the optional post-processing rules in a fixed order and maintains the time state those rules require |
| `Configuration` | `ModuleConfig` and `PostProcessingSettings` define the configuration model and defaults. `ConfigStore` handles reading and writing, `ConfigValidator` handles validation and fallback, and `LegacyImporter` performs read-only migration of old configurations |
| `Runtime` | `SourceCoordinator` creates the data sources and selects one of them. `TrackingPipeline` converts the selected snapshot into output and handles stale data. `DiagnosticReporter` writes the status log every 5 seconds |
| `Sdk` | `UnifiedPicoTrackingModule` derives from VRCFT's `ExtTrackingModule` and implements initialization, update and teardown |

### SDK boundary

Only the `Sdk` directory references VRCFaceTracking and `Microsoft.Extensions.Logging`; no other layer references any SDK type. The layers exchange data through the types in `Core`, and the module's `ExpressionChannel` values are converted by name to VRCFT's `UnifiedExpressions` in the `Sdk` layer.

`UnifiedPicoTrackingModule.Write` is the only place that writes to `UnifiedTracking.Data`. It writes only the outputs the module has been granted. If the host has not assigned eye output to the module, or eye tracking is disabled in the configuration, the eye expression channels and gaze data are not written; the same applies to face output.

### Runtime data flow

1. Each data source receives UDP data on its own background thread, decodes and validates it, and publishes the result as a new immutable snapshot. Network operations never run on VRCFT's initialization or update thread.
2. Each time VRCFT calls `Update`, `SourceCoordinator` reads the latest snapshot of each source and determines which source is selected.
3. `TrackingPipeline` performs the staleness check, expression mapping, gaze resolution and post-processing, and produces a `TrackingOutput`.
4. The `Sdk` layer writes the output to `UnifiedTracking` and calls `DiagnosticReporter` to write the status log. Each `Update` call ends with a 2 ms sleep.

## Default configuration

Defaults are defined in `Configuration/ModuleConfig.cs` and `Configuration/PostProcessingSettings.cs`. `config/UnifiedPicoModule.example.json` should match what the configuration model exports. After changing a default, export a new default configuration with the configuration tool and compare it with the example:

```powershell
dotnet tools/ConfigTool/bin/Release/net10.0/UnifiedPicoConfigTool.dll defaults <output-path.json>
```

The command never overwrites an existing file; the output path must not exist yet.

## PC tongue checks

After building the pinned SDK, run `./tools/test-tongue.ps1`. It tests the production receiver and SDK ownership using ephemeral loopback sockets, without installing or starting a host. The paired app and cross-language wire test are documented in [PC tongue development](../TONGUE.md).

## Contributing

See [Contributing](../CONTRIBUTING.md) for how to take part. A successful build only shows that the code compiles. Compatibility with devices, sender versions and avatars has to be confirmed separately on real hardware.
