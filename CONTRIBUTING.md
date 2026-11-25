# Contributing to UnifiedPicoModule

English | [简体中文](CONTRIBUTING_zh.md) · [README](README.md) · [Development Guide](docs/development.md)

Contributions, bug reports, and hardware test feedback are welcome. Before opening a PR, please read the [development guide](docs/development.md), [protocol reference](docs/protocol.md), and [privacy guide](docs/privacy.md).

## Clean-Room & Third-Party Licensing Rule

> [!IMPORTANT]
> Never copy, translate, or adapt code or assets from repositories without an explicit open-source license.

- **License-unclear upstream repositories:** In the snapshots reviewed by this project, `PicoStreamingAssistantFTUDP` (SAFT) and `lonelyicer/VRCFTPicoModule` have no clear license. Their implementations must never be copied, ported, or paraphrased into this codebase.
- **Independent implementation from specs/behavior:** When implementing support based on public wire formats or observed behavior, write the code independently and record the source URL, reviewed commit/version, and scope in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and [THIRD_PARTY_NOTICES_zh.md](THIRD_PARTY_NOTICES_zh.md).
- **Clearly licensed reuse:** If reusing code or assets with a compatible license, document the source, exact version, and file scope, and preserve all license and copyright notices.

## Reporting Issues

When filing a bug report or hardware compatibility result, include:

- Steps to reproduce and observed vs. expected behavior
- UnifiedPicoModule version and VRCFT version (e.g., `5.4.5` or Steam `6.0.0.0`)
- Sender and version (e.g., `PicoFacialBridge 0.5.0`, `PICO Connect 10.6.6`)
- Headset model (e.g., `PICO 4 Pro`)
- Relevant lines from VRCFT's Output log, including the 5-second status and `CONSUMPTION` lines
- For config issues: `UnifiedPicoModule.json` or the output of `UnifiedPicoConfigTool validate`

### Sanitize Logs and Configs First

Before uploading any file or log, strip:

- Windows usernames in local paths
- Headset and PC LAN IP addresses
- The `Migration` block in `UnifiedPicoModule.json` (contains old absolute paths)
- Raw tracking dumps and unfiltered `.pcap`/`.pcapng` packet captures (keep only the specific fields relevant to the issue)

## Architecture & Coding Expectations

Keep the layer boundaries intact (full map in [docs/development.md](docs/development.md#architecture)):

- **SDK isolation:** Only `Sdk/` may reference `VRCFaceTracking.*` or `Microsoft.Extensions.Logging`. `tools/ConfigTool` compiles only `Core/Contracts.cs` and `Configuration/*.cs`.
- **Decoupled stages:** Protocol decoding (`Protocol/`), source selection (`Runtime/SourceCoordinator.cs`), eye resolution (`Mapping/EyeResolver.cs`), and post-processing (`Processing/PostProcessor.cs`) stay independent; changing one should not leak into another.
- **Explicit configuration only:** `UnifiedPicoModule.json` is the sole source of module behavior. Do not add hidden runtime presets in code or rewrite a user's JSON automatically based on the active sender.
- **Warnings as errors:** Match surrounding style and keep builds warning-free (`TreatWarningsAsErrors` is enabled). Add comments only where non-obvious constraints or protocol edge cases need explanation.

### Configuration Changes

When adding, renaming, or changing a configuration field, range, or default:

1. Update the model in `Configuration/` and validation rules in `Configuration/ConfigValidator.cs`.
2. Update `Configuration/LegacyImporter.cs` if migration behavior is affected.
3. Re-export `config/UnifiedPicoModule.example.json` using `UnifiedPicoConfigTool.dll defaults`.
4. Update both English and Chinese configuration/post-processing docs.
5. Note in the PR/commit description whether `ConfigVersion` needs to be incremented if existing field semantics changed.

## Testing & Verification

Run both the release build and the dual-host compatibility suite locally before opening a PR:

```powershell
./tools/build.ps1 -Configuration Release
pwsh -NoProfile -File tools/test-compatibility.ps1
```

In your PR or commit description:

- List what you actually ran (build, compatibility tests, real-headset testing) and what remains unverified. A green build does not prove hardware or avatar compatibility.
- Clearly label any sender/version combinations not verified on real hardware as unverified, and any uncalibrated coefficients as uncalibrated (never as recommended values). Current verified setups are listed in [docs/protocol.md](docs/protocol.md#verification-status).

## Pull Request Checklist

- [ ] Clean-room rule followed; `THIRD_PARTY_NOTICES*.md` updated if new protocol references were consulted
- [ ] `./tools/build.ps1 -Configuration Release` succeeds with zero warnings
- [ ] `pwsh -NoProfile -File tools/test-compatibility.ps1` passes for both VRCFT 5.4.5 and Steam VRCFT 6
- [ ] `config/UnifiedPicoModule.example.json` regenerated if defaults changed
- [ ] English and Chinese docs updated together
- [ ] Verified vs. unverified scope stated accurately in the PR description
