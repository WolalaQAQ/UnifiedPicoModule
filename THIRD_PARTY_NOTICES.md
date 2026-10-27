# Third-party sources and licenses

English | [简体中文](THIRD_PARTY_NOTICES_zh.md)

This file records the third-party projects this project refers to, its build and runtime dependencies, and the license boundaries that apply to each. It distinguishes reference sources from actual reuse: referring to a project is not the same as distributing its code, and this project's MIT license does not replace any third-party license.

The records below are based on the versions reviewed on 2026-10-01. If code, dependencies or assets are added later, the attribution and redistribution notes in this file should be updated at the same time.

## Original content of this project

This project's configuration, decoding, mapping and post-processing are implemented from published interface facts and independently written specifications. No classes from the earlier modules, no unclear-license `Updater` and no SDK implementation were carried over. The original code and documentation are licensed under [MIT](LICENSE), with the following copyright notice:

```text
Copyright (c) 2026 UnifiedPicoModule contributors
```

The module package includes the full MIT license text. This license applies only to original content that this project has the right to license. It does not re-grant any rights to third-party code, SDKs, trademarks or assets.

## Reference sources

### PC tongue integration

The development TJR1 receiver replaces the headset receiver proposed in
[Rulez777's PR #1](https://github.com/WolalaQAQ/UnifiedPicoModule/pull/1), reviewed
at `f91d18d46e5661ea9e205eee9d854d965c4c99bc`. Its loopback wire format, source-age
state machine and lifecycle tests are this project's implementation. The old
`Sdk/TongueReceiver.cs` and TNG2 subscription are not copied. The paired
PicoET-Enhance PC app documents its model/code attribution in its own repository.

This project is an independent implementation. It does not copy the unclear-license code from SAFT or lonelyicer, and it does not work around licensing requirements by renaming, translating or AI-rewriting that code. Neither the implementations nor the images from SAFT and lonelyicer are included in this project. Renaming, translating or AI-rewriting does not constitute a license.

The assessments in this section apply only to the snapshots that were reviewed. They do not claim that the authors have never granted a license through other channels.

### PicoFacialDataModule

- Author: thoricelli
- Repository: https://github.com/thoricelli/PicoFacialDataModule
- Reviewed version: `0ab66348bf970763322fe261b54445bcc5a2b23e` (2026-09-04)
- License: MIT, see the [LICENSE at the pinned commit](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/LICENSE)
- Copyright notice:

  ```text
  Copyright (c) 2026 thoricelli
  ```

- Use: comparison for the legacy 536 protocol, configuration and behavior.
- Boundary: reusing a specific implementation or a substantial portion requires keeping thoricelli's copyright notice and the full MIT license text.

### Split-module configuration revision

- Source identifier: import identifier `bridge-current-0.1-beta-split-fix1`; there is no pinned public commit.
- Use: comparison for `ShapeGain`, `BridgeMode` and split configuration behavior.
- Note: this identifier is not an alias of any pinned thoricelli commit. This project does not require users to have a particular directory layout.

### PicoFacialBridge public baseline

- Authors: WolalaQAQ and PicoFacialBridge contributors
- Repository: https://github.com/WolalaQAQ/PicoFacialBridge
- Reviewed version: [`836101c27feffccfbf7df6d94af9e98093f96057`](https://github.com/WolalaQAQ/PicoFacialBridge/tree/836101c27feffccfbf7df6d94af9e98093f96057)
- License: MIT, see the [LICENSE at the pinned commit](https://github.com/WolalaQAQ/PicoFacialBridge/blob/836101c27feffccfbf7df6d94af9e98093f96057/LICENSE)
- Copyright notices:

  ```text
  Copyright (c) 2026 WolalaQAQ and PicoFacialBridge contributors
  Copyright (c) 2026 thoricelli
  ```

- Use: factual basis for the legacy 536 prefix and the control protocol.
- Boundary: this public version does not demonstrate support for split or enhanced mode.

### PicoFacialBridge split and enhanced extensions

- Reviewed material: the `TrackingData`, `TrackingMode`, `PeerProtocol` and `BridgeService` extension interfaces reviewed on 2026-10-01; there is no citable pinned public commit.
- Use: basis for BridgeSplit and enhanced mode detection.
- Note: this entry is recorded separately from the public baseline. The interface contract and the limits of the evidence are described in the [protocol reference](docs/protocol.md). This project does not require users to hold a particular source directory.

### PicoStreamingAssistantFTUDP (SAFT)

- Author: regzo2
- Repository: https://github.com/regzo2/PicoStreamingAssistantFTUDP
- Reviewed version: branch `vrcfacetracking-module`, [`ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7`](https://github.com/regzo2/PicoStreamingAssistantFTUDP/tree/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7) (2026-05-05)
- License: the research record shows no LICENSE or COPYING file. The absence of a license does not mean the code is in the public domain.
- Use: reference for the published data layout, interfaces and configuration facts.
- Boundary: the implementation, large blocks of mapping code and assets must not be copied.

### VRCFTPicoModule (pikepikeid)

- Author: pikepikeid
- Repository: https://github.com/pikepikeid/VRCFTPicoModule
- Reviewed version: `c026aa455da60dd50492b3d36c9d360d0c343b8e` (2026-09-29)
- License: the LICENSE at this commit declares MIT, see the [LICENSE at the pinned commit](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/LICENSE)
- Copyright notices:

  ```text
  Copyright (c) 2026 pikepikeid
  Copyright (c) 2026 WolalaQAQ and PicoFacialBridge contributors
  Copyright (c) 2026 thoricelli
  ```

- Use: reference for the colon-separated INI format, known wire formats and post-processing behavior.
- Boundary: its inherited sources require separate review. This LICENSE does not automatically license the parts that originate from lonelyicer.

### VRCFTPicoModule (lonelyicer)

- Author: lonelyicer
- Repository: https://github.com/lonelyicer/VRCFTPicoModule
- Reviewed version: [`5046c9b726e33e461e7f389c73d94db26ae7c7f8`](https://github.com/lonelyicer/VRCFTPicoModule/tree/5046c9b726e33e461e7f389c73d94db26ae7c7f8)
- License: no explicit license was found in the reviewed snapshot.
- Boundary: the implementation and assets must not be copied. Adding a LICENSE file in a downstream fork is not sufficient to show that the original author granted a license for the inherited code.

### ALVR attribution comments

The "Taken from ALVR" attribution comments in the earlier modules do not mean that this project copied ALVR code. If such code is reused in the future, the specific commit, files, copyright and license must be recorded separately. A vague attribution must not be carried over, and the code must not be covered by a single blanket MIT file.

## MIT license text

The copyright notices listed above are taken from the original LICENSE files of the research sources. Keeping them here does not mean that this module contains those implementations. Copying or adapting substantial code requires keeping the applicable copyright notices and the full license text; a web link alone is not sufficient. These notices cover only what each rights holder is entitled to license, and they cannot fill the licensing gaps in inherited code such as lonelyicer's.

The MIT license text used by these LICENSE files is as follows:

```text
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Build and runtime dependencies

### VRCFaceTracking SDK / Core

- Author: benaclejames
- Repository: https://github.com/benaclejames/VRCFaceTracking
- Development baseline: [`7add14873e398c4095d06913cdeee7a01fc43fab`](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab), net10.0, AssemblyVersion 5.4.5.0
- License: Apache-2.0, see the [LICENSE at the pinned commit](https://github.com/benaclejames/VRCFaceTracking/blob/7add14873e398c4095d06913cdeee7a01fc43fab/LICENSE) and the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- Copyright notice: the license's copyright appendix reads Copyright 2024 benaclejames
- Use: references to the SDK / Core interfaces.

AssemblyVersion is not a substitute for a pinned source commit, and it does not mean that every host of the same version has passed runtime acceptance.

The source project `UnifiedPicoModule.csproj` sets the VRCFaceTracking.SDK and VRCFaceTracking.Core references to `Private=false`. The module package does not distribute SDK / Core DLLs, does not override or bundle the host, and does not install anything automatically. How to obtain the SDK source and how its version is pinned for builds is described in the [development guide](docs/development.md).

Using the interfaces independently does not require relicensing the whole original module under Apache-2.0. Copying or modifying SDK implementations or samples, or distributing its DLLs, requires separate compliance with Apache-2.0: include the license text, keep the applicable copyright and attribution notices, mark modified upstream files prominently, and include the applicable contents of any NOTICE file that accompanies the corresponding distribution. The SDK must not be relabeled as MIT, and the fact that the SDK is not packaged is not a reason to disregard the obligations that arise from any code actually copied.

This file is not a redistribution license addendum for the Apache-2.0 DLLs. The current module package does not distribute these DLLs. If the contents of the package change, they must be reviewed again, and the full license text and the applicable NOTICE must be included.

### Microsoft.Extensions.Logging.Abstractions

- Author: Microsoft
- Version: [10.0.1](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.1)
- License: MIT (license expression in the NuGet metadata)
- Copyright notice: Copyright © Microsoft Corporation
- Source repository: [dotnet/dotnet pinned commit](https://github.com/dotnet/dotnet/tree/fad253f51b461736dfd3cd9c15977bb7493becef)
- Dependencies: the net10 target depends on `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.1

The module project references this package with `PrivateAssets=all`. `PrivateAssets` is a dependency-flow setting. It does not guarantee that the files are never copied, and it is not a license. The module package is assembled from a file allowlist and contains no logging or other NuGet DLLs. The required runtime dependencies are provided by the host; the module has been run on official VRCFT 5.4.5, and other host versions have not been tested. Distributing these components instead would require a separate review of copyrights, licenses and addenda. Microsoft's copyright is not held by this project.

## Assets and future updates

This project introduces no third-party icons or images, and the documentation uses no third-party icons, images or trademarked assets. If assets, dependencies or code reuse are added later, this file should record the source URL, commit or version, file scope, full license text, copyright, description of modifications, and whether the material ships with the module package. All third-party content must not be covered by a single blanket MIT file.

## Pinned source entry points

- [thoricelli LICENSE](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/LICENSE)
- [SAFT pinned source tree](https://github.com/regzo2/PicoStreamingAssistantFTUDP/tree/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7)
- [pike LICENSE](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/LICENSE)
- [lonelyicer research snapshot](https://github.com/lonelyicer/VRCFTPicoModule/tree/5046c9b726e33e461e7f389c73d94db26ae7c7f8)
- [VRCFT pinned commit](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab)
- [Protocol offsets and capability boundaries](docs/protocol.md), [migration and unreproduced differences](docs/configuration.md), [independent post-processing semantics](docs/postprocessing.md)

## Trademarks

Project names in this file are used only to identify interoperability targets and do not imply official endorsement or sponsorship by PICO, VRCFaceTracking or any of the authors.
