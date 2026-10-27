# 第三方来源与许可

[English](THIRD_PARTY_NOTICES.md) | 简体中文

本文件记录本项目参考的第三方项目、构建与运行时依赖，以及各自的许可边界。文中区分“参考来源”与“实际复用”：参考某个项目并不等于分发其代码，本项目采用的 MIT 许可证也不替代任何第三方许可证。

以下记录基于 2026-10-01 核查的版本。今后如新增代码、依赖或资源，应同步更新本文件中的归属与再分发说明。

## 本项目原创内容

本项目的配置、解码、映射与后处理依据公开的接口事实和独立编写的规格实现，没有沿用旧模块的类、许可不明的 `Updater` 或 SDK 的实现。原创代码与文档采用 [MIT](LICENSE) 许可证，版权声明如下：

```text
Copyright (c) 2026 UnifiedPicoModule contributors
```

模块包附带 MIT 许可证全文。该许可证仅适用于本项目有权许可的原创内容，不对第三方代码、SDK、商标或资源重新授予任何权利。

## 参考来源

### PC舌头整合

开发版TJR1接收器替换[Rulez777的PR #1](https://github.com/WolalaQAQ/UnifiedPicoModule/pull/1)中的头显接收方案，审阅版本为`f91d18d46e5661ea9e205eee9d854d965c4c99bc`。环回线格式、源年龄状态机及生命周期测试为本项目实现，未复制旧`Sdk/TongueReceiver.cs`或TNG2订阅。配套PicoET-Enhance PC App在其仓库记录模型及代码归因。

本项目采用独立实现，不复制 SAFT 与 lonelyicer 中许可不明的代码，也不通过改名、翻译或 AI 改写规避许可要求。SAFT 与 lonelyicer 的实现和图片均未纳入本项目。改名、翻译或 AI 改写不构成授权。

本节的判断仅针对所核查的快照，并不断言相关作者从未通过其他渠道授予许可。

### PicoFacialDataModule

- 作者：thoricelli
- 仓库：https://github.com/thoricelli/PicoFacialDataModule
- 核查版本：`0ab66348bf970763322fe261b54445bcc5a2b23e`（2026-09-04）
- 许可证：MIT，见[固定提交的 LICENSE](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/LICENSE)
- 版权声明：

  ```text
  Copyright (c) 2026 thoricelli
  ```

- 用途：对照旧版 536 协议、配置与行为。
- 边界：如复用具体实现或实质性片段，须保留 thoricelli 的版权声明及完整的 MIT 许可证文本。

### 分流模块配置改版

- 来源标识：导入标识 `bridge-current-0.1-beta-split-fix1`，没有固定的公开提交。
- 用途：对照 `ShapeGain`、`BridgeMode` 与分流配置的行为。
- 说明：该标识不是 thoricelli 某个固定提交的别名。本项目不要求用户具备特定的目录结构。

### PicoFacialBridge 公开基线

- 作者：WolalaQAQ 及 PicoFacialBridge contributors
- 仓库：https://github.com/WolalaQAQ/PicoFacialBridge
- 核查版本：[`836101c27feffccfbf7df6d94af9e98093f96057`](https://github.com/WolalaQAQ/PicoFacialBridge/tree/836101c27feffccfbf7df6d94af9e98093f96057)
- 许可证：MIT，见[固定提交的 LICENSE](https://github.com/WolalaQAQ/PicoFacialBridge/blob/836101c27feffccfbf7df6d94af9e98093f96057/LICENSE)
- 版权声明：

  ```text
  Copyright (c) 2026 WolalaQAQ and PicoFacialBridge contributors
  Copyright (c) 2026 thoricelli
  ```

- 用途：作为旧版 536 前缀与控制协议的事实依据。
- 边界：该公开版本不能证明支持分流或增强模式。

### PicoFacialBridge 分流与增强扩展

- 核查对象：2026-10-01 核查的 `TrackingData`、`TrackingMode`、`PeerProtocol`、`BridgeService` 扩展接口，没有可引用的固定公开提交。
- 用途：作为 BridgeSplit 与增强模式判别的依据。
- 说明：本条与公开基线分开记录，接口契约与证据的局限见[协议参考](docs/protocol_zh.md)。本项目不要求用户持有特定的源码目录。

### PicoStreamingAssistantFTUDP（SAFT）

- 作者：regzo2
- 仓库：https://github.com/regzo2/PicoStreamingAssistantFTUDP
- 核查版本：分支 `vrcfacetracking-module`，[`ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7`](https://github.com/regzo2/PicoStreamingAssistantFTUDP/tree/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7)（2026-05-05）
- 许可证：研究记录中未见 LICENSE 或 COPYING 文件。没有许可证不等于进入公共领域。
- 用途：参考公开的数据布局、接口与配置事实。
- 边界：不得复制其实现、大段映射代码或资源。

### VRCFTPicoModule（pikepikeid）

- 作者：pikepikeid
- 仓库：https://github.com/pikepikeid/VRCFTPicoModule
- 核查版本：`c026aa455da60dd50492b3d36c9d360d0c343b8e`（2026-09-29）
- 许可证：该提交的 LICENSE 声明为 MIT，见[固定提交的 LICENSE](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/LICENSE)
- 版权声明：

  ```text
  Copyright (c) 2026 pikepikeid
  Copyright (c) 2026 WolalaQAQ and PicoFacialBridge contributors
  Copyright (c) 2026 thoricelli
  ```

- 用途：参考冒号 INI 格式、已知线格式与后处理行为。
- 边界：其继承来源须另行核对。该 LICENSE 不会自动为其中来自 lonelyicer 的部分授予许可。

### VRCFTPicoModule（lonelyicer）

- 作者：lonelyicer
- 仓库：https://github.com/lonelyicer/VRCFTPicoModule
- 核查版本：[`5046c9b726e33e461e7f389c73d94db26ae7c7f8`](https://github.com/lonelyicer/VRCFTPicoModule/tree/5046c9b726e33e461e7f389c73d94db26ae7c7f8)
- 许可证：核查快照中未见明确许可。
- 边界：不得复制其实现或资源。下游 fork 补充 LICENSE 文件，不足以证明原作者已对被继承的代码授予许可。

### ALVR 归属注释

旧模块中 “Taken from ALVR” 的归属注释，不表示本项目复制了 ALVR 的代码。如今后确需复用，应另行记录具体的提交、文件、版权与许可证，不得沿用含糊的归属说明，也不得以一份总括性的 MIT 文件覆盖。

## MIT 许可证全文

上文列出的版权声明摘自各研究来源的原始 LICENSE，保留在此并不表示本模块包含这些实现代码。如实际复制或改编实质性代码，须保留适用的版权声明与完整许可证文本，仅提供网页链接是不够的。这些声明仅覆盖各权利人有权许可的部分，不能弥补 lonelyicer 等继承代码的许可缺口。

各来源 LICENSE 采用的 MIT 许可证原文如下：

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

## 构建与运行时依赖

### VRCFaceTracking SDK / Core

- 作者：benaclejames
- 仓库：https://github.com/benaclejames/VRCFaceTracking
- 开发基线：[`7add14873e398c4095d06913cdeee7a01fc43fab`](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab)，net10.0，AssemblyVersion 5.4.5.0
- 许可证：Apache-2.0，见[固定提交的 LICENSE](https://github.com/benaclejames/VRCFaceTracking/blob/7add14873e398c4095d06913cdeee7a01fc43fab/LICENSE) 与 [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- 版权声明：许可证的版权附录为 Copyright 2024 benaclejames
- 用途：引用 SDK / Core 的接口。

AssemblyVersion 不能替代源码提交的固定版本，也不代表所有同版本的宿主都已通过运行验收。

源码工程 `UnifiedPicoModule.csproj` 将 VRCFaceTracking.SDK 与 VRCFaceTracking.Core 的引用设为 `Private=false`。模块包不分发 SDK / Core 的 DLL，不覆盖或捆绑宿主，也不执行自动安装。SDK 源码的获取方式与构建时的版本固定见[开发说明](docs/development_zh.md)。

仅独立使用其接口，不要求将整个原创模块改为 Apache-2.0 许可。如复制或修改 SDK 的实现或示例，或分发其 DLL，则须另行遵守 Apache-2.0：附上许可证文本，保留适用的版权与归属声明，在修改过的上游文件中作醒目标注，并在相应分发附带 NOTICE 文件时一并提供其中适用的内容。SDK 不得改标为 MIT，也不能以“未打包 SDK”为由忽略实际复制代码所产生的义务。

本文件不是 Apache-2.0 DLL 的再分发许可附件。当前模块包不分发这些 DLL。如模块包的组成发生变化，须重新核查，并附上许可证全文与适用的 NOTICE。

### Microsoft.Extensions.Logging.Abstractions

- 作者：Microsoft
- 版本：[10.0.1](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.1)
- 许可证：MIT（NuGet 元数据中的 license expression）
- 版权声明：Copyright © Microsoft Corporation
- 源码仓库：[dotnet/dotnet 固定提交](https://github.com/dotnet/dotnet/tree/fad253f51b461736dfd3cd9c15977bb7493becef)
- 依赖：net10 目标依赖 `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.1

模块工程以 `PrivateAssets=all` 引用该包。`PrivateAssets` 是依赖传递的设置，并不保证相关文件不会被复制，也不是许可证。模块包按文件白名单打包，不包含 logging 或其他 NuGet DLL，所需的运行时依赖由宿主提供；模块已在官方 VRCFT 5.4.5 上运行，其他宿主版本尚未测试。如改为分发这些组件，须另行核对版权、许可证与附件。Microsoft 的版权不归本项目所有。

## 资源与后续更新

本项目未引入第三方图标或图片，文档中也不使用第三方图标、图片或商标素材。今后如新增素材、依赖或复用代码，应在本文件中记录来源 URL、提交或版本、文件范围、许可证原文、版权、修改说明，以及是否随模块包分发。不得以一份总括性的 MIT 文件覆盖全部第三方内容。

## 固定来源入口

- [thoricelli LICENSE](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/LICENSE)
- [SAFT 固定源码树](https://github.com/regzo2/PicoStreamingAssistantFTUDP/tree/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7)
- [pike LICENSE](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/LICENSE)
- [lonelyicer 研究快照](https://github.com/lonelyicer/VRCFTPicoModule/tree/5046c9b726e33e461e7f389c73d94db26ae7c7f8)
- [VRCFT 固定提交](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab)
- [协议偏移与能力边界](docs/protocol_zh.md)、[迁移与未复现差异](docs/configuration_zh.md)、[独立后处理语义](docs/postprocessing_zh.md)

## 商标说明

文中出现的项目名称仅用于说明互操作对象，不代表 PICO、VRCFaceTracking 或各作者的官方认可或赞助。
