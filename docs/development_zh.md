# 开发说明

[English](development.md) | 简体中文 · [README](../README_zh.md)

本文说明 UnifiedPicoModule 的构建方式、打包内容、发布流程和代码结构。

## 环境要求

- Windows 与 PowerShell
- .NET 10 SDK
- Git
- 固定版本的 VRCFaceTracking 源码

构建不依赖 Android 工具链或其他 PICO 项目。构建和打包脚本不会安装模块，不会向 VRCFT 的 `CustomLibs` 目录复制文件，也不会修改设备或串流软件的设置。

## 构建

### 准备 SDK 源码

模块基于 VRCFaceTracking 提交 `7add14873e398c4095d06913cdeee7a01fc43fab` 编译，对应程序集版本 `5.4.5.0`，目标框架 `net10.0`。在仓库根目录执行：

```powershell
git clone https://github.com/benaclejames/VRCFaceTracking.git .sdk/source/VRCFaceTracking
git -C .sdk/source/VRCFaceTracking switch --detach 7add14873e398c4095d06913cdeee7a01fc43fab
```

`.sdk/` 是本地缓存目录，已被 `.gitignore` 排除。

### 运行构建脚本

```powershell
./tools/build.ps1 -Configuration Release
```

`-Configuration` 可选 `Debug` 或 `Release`，默认为 `Release`。SDK 源码目录按以下顺序确定：

1. `-SdkSource` 参数；
2. 环境变量 `VRCFT_SDK_SOURCE`；
3. 仓库内的 `.sdk/source/VRCFaceTracking`。

脚本不会自动下载源码。找不到源码目录时，脚本直接报错退出。

### 构建步骤

`tools/build.ps1` 依次执行以下操作，任何一步失败都会终止构建：

1. 确认源码目录的 `HEAD` 为上述固定提交。
2. 确认源码目录中没有对已跟踪文件的修改。脚本不会丢弃或还原这些修改。
3. 计算四个关键文件的 SHA256 指纹并与脚本中记录的值比对：`Directory.Build.props`、`Directory.Packages.props`、`VRCFaceTracking.SDK/ExtTrackingModule.cs` 和 `VRCFaceTracking.Core/Params/Expressions/UnifiedExpressions.cs`。计算前去掉 UTF-8 BOM，并将 CRLF 统一为 LF，因此 Git 换行设置不同不会导致误判。
4. 以 Release 配置构建 `VRCFaceTracking.Core`，所有中间文件和输出都写入 `.sdk/build`，不写入 SDK 源码目录。
5. 确认 `VRCFaceTracking.Core.dll` 与 `VRCFaceTracking.SDK.dll` 的程序集版本为 `5.4.5.0`，然后将两者复制到 `.sdk/`。
6. 写入 `.sdk/sdk-provenance.json`，记录提交、程序集版本、目标框架、指纹归一化方式和四个文件的指纹。
7. 构建模块项目 `UnifiedPicoModule.csproj`。
8. 构建配置工具 `tools/ConfigTool/ConfigTool.csproj`。

如需更换 SDK 版本，应同时更新脚本中的提交与指纹、`release.yml` 中的 `VRCFT_COMMIT`，以及 `UnifiedPicoModule.csproj` 中的版本检查，并将其作为一次独立的兼容性变更处理。

### 构建产物

| 产物 | 路径 |
|---|---|
| 模块 | `bin/<Configuration>/net10.0/UnifiedPicoModule.dll` |
| 配置工具 | `tools/ConfigTool/bin/<Configuration>/net10.0/UnifiedPicoConfigTool.dll` |
| SDK 程序集 | `.sdk/VRCFaceTracking.Core.dll`、`.sdk/VRCFaceTracking.SDK.dll` |
| SDK 来源记录 | `.sdk/sdk-provenance.json` |

## 项目文件

### UnifiedPicoModule.csproj

- 目标框架 `net10.0`，启用可空引用类型和隐式 using，所有警告视为错误。
- 版本号由 `Version` 属性指定，当前为 `0.4.0`。
- 通过 `HintPath` 引用 `$(VrcftSdkDir)` 下的 `VRCFaceTracking.SDK.dll` 和 `VRCFaceTracking.Core.dll`，并设置 `Private=false`，因此这两个 DLL 不会复制到输出目录。`VrcftSdkDir` 默认为仓库内的 `.sdk`，也可以通过 MSBuild 属性覆盖。
- `CheckSdk` 目标在解析引用前检查这两个 DLL 是否存在，并要求程序集版本为 `5.4.5.0`。
- 引用 `Microsoft.Extensions.Logging.Abstractions` 10.0.1，设置为 `PrivateAssets="all"`。
- 从编译中排除 `tools/`、`.sdk/`、`artifacts/`、`docs/` 和 `reference/` 下的源文件。
- 将 `assets/branding/module-icon.png` 作为嵌入资源，逻辑名为 `UnifiedPicoModule.Branding.ModuleIcon.png`，用作模块列表中的图标。

### Directory.Build.props

通过 `PathMap` 将项目目录映射为 `/_/UnifiedPicoModule/`，编译产物中记录的源文件路径因此不包含构建机的本地目录。该设置同时作用于模块和配置工具。

### tools/ConfigTool/ConfigTool.csproj

配置工具是一个独立的控制台程序，程序集名为 `UnifiedPicoConfigTool`，目标框架 `net10.0`。项目关闭了默认编译项，只编译 `Program.cs`，并以链接方式引入 `Core/Contracts.cs` 和 `Configuration/*.cs`。配置工具不引用 VRCFT SDK，配置的读取、校验和迁移逻辑与模块共用同一份源码。

配置工具提供 `validate`、`defaults` 和 `import` 三个命令，用法请参阅[配置工具说明](../tools/ConfigTool/README_zh.md)。

### module.json

VRCFT 读取的模块元数据，包含以下字段：

| 字段 | 当前值或说明 |
|---|---|
| `ModuleId` | `8322e16d-c38b-42f0-8d35-68f27227b442`，同时决定模块在 `CustomLibs` 下的目录名 |
| `Version` | `0.4.0`，必须与 `UnifiedPicoModule.csproj` 中的版本一致 |
| `ModuleName` | `Unified PICO Face Tracking` |
| `ModuleDescription` / `UsageInstructions` | 模块列表中显示的说明文字 |
| `DownloadUrl` | 当前为空 |
| `ModulePageUrl` | 项目的 GitHub 仓库地址 |
| `DllFileName` | `UnifiedPicoModule.dll` |

## 打包

```powershell
./tools/package.ps1
```

脚本默认先调用 `tools/build.ps1 -Configuration Release`，并接受同样的 `-SdkSource` 参数和 `VRCFT_SDK_SOURCE` 环境变量。指定 `-SkipBuild` 时跳过构建，直接使用已有的 Release 产物，此时无法保证这些产物与当前源码一致。

### ModulePageUrl 检查

打包前，脚本要求 `module.json` 中的 `ModulePageUrl` 是非空的绝对 URI，协议为 `http`、`https` 或 `file`，否则拒绝打包。原因是 VRCFT 5.4.5 将该字段直接绑定到 `HyperlinkButton.NavigateUri`，在实机验证中，空字符串和 `null` 都会导致 Module Registry 页面崩溃。本地实验性构建可以使用占位地址，但正式发布应使用项目的实际页面地址。

### 包内容

模块包为扁平结构，根目录即模块目录，包含以下内容：

- `UnifiedPicoModule.dll`
- `module.json`
- `UnifiedPicoModule.json`：由配置工具的 `defaults` 命令从配置模型导出的默认配置
- `config/UnifiedPicoModule.example.json`：与上述默认配置内容相同
- `sdk-provenance.json`
- `README.md`、`README_zh.md`、`CHANGELOG.md`、`CHANGELOG_zh.md`、`CONTRIBUTING.md`、`CONTRIBUTING_zh.md`、`THIRD_PARTY_NOTICES.md`、`THIRD_PARTY_NOTICES_zh.md`、`LICENSE`
- `docs/` 下的配置说明、后处理参考、协议参考、开发说明、隐私与网络安全，各含中英文版本
- `tools/ConfigTool/README.md`、`tools/ConfigTool/README_zh.md`
- `assets/branding/` 下的 `logo-dark`、`logo-light`、`module-icon` 三组图标，各含 SVG 与 PNG

所有文件都按白名单逐个复制，`docs/` 目录不会被递归复制。默认配置始终重新导出，不读取开发者本机的 `UnifiedPicoModule.json`。包内不包含 VRCFaceTracking 的 DLL、SDK 源码、配置工具程序和 PDB 文件。

### 输出文件

每次打包都会在 `artifacts/` 下生成一个以时间戳命名的暂存目录 `stage-<yyyyMMdd-HHmmss-fff>`，以及：

- `UnifiedPicoModule-<Version>-<yyyyMMdd-HHmmss-fff>.zip`
- 同名的 `.zip.sha256`，内容为 `<SHA256>  <文件名>`

文件名中的时间戳使每次打包的输出互不覆盖。脚本不会清理以前的输出，需要时可手动删除 `artifacts/` 下的旧文件。

## 发布

`.github/workflows/release.yml` 在向 GitHub 推送以 `v` 开头的标签时运行。普通的分支推送和 Pull Request 不会触发该流程。

### 发布前的准备

1. 将 `module.json` 和 `UnifiedPicoModule.csproj` 中的 `Version` 设为同一版本号，不带 `v` 前缀。
2. `Sdk/UnifiedPicoTrackingModule.cs` 中的启动日志以字面量写出了版本号，需要一并修改。
3. 将 `module.json` 中的 `ModulePageUrl` 设为项目的实际页面地址。
4. 提交并推送上述修改，然后为该提交创建并推送标签：

```powershell
git tag -a v0.4.0 -m "UnifiedPicoModule 0.4.0"
git push origin v0.4.0
```

标签格式为 `vMAJOR.MINOR.PATCH`，可以附加 SemVer 预发布后缀，例如 `v0.4.0-rc.1`。不接受 `+build` 构建元数据，数字部分和数字型预发布标识不允许前导零。

### 构建任务

构建任务在 `windows-latest` 上运行，权限为 `contents: read`，依次执行：

1. 检出仓库，不保留凭据。
2. 校验标签格式，并确认标签版本与 `module.json`、`UnifiedPicoModule.csproj` 中的版本完全一致。
3. 安装 .NET 10 SDK。
4. 将 VRCFaceTracking 的固定提交检出到 `.sdk/source/VRCFaceTracking`。该提交记录在工作流的 `VRCFT_COMMIT` 环境变量中，`tools/build.ps1` 会再次独立校验。
5. 运行 `tools/package.ps1 -SdkSource .sdk/source/VRCFaceTracking`。
6. 检查生成的 ZIP：路径中不得出现 `agents`、`archive`、`private`、`.sdk`、`bin`、`obj` 目录，或包含 `migration`、`current-settings` 的名称，不得包含 `.log`、`.pcap`、`.pcapng`、`.har`、`.pdb` 文件，除 `UnifiedPicoModule.dll` 外不得包含其他 DLL。包内的默认配置中 `Bridge.IP`、`Streaming.IP` 和 `Migration` 必须为空，且 `PostProcessing` 下除 `ChannelGain` 外不得有 `Enabled` 为 `true` 的规则。
7. 将 ZIP 复制为不带时间戳的 `UnifiedPicoModule-<version>.zip`，重新计算 SHA256，并生成 `.zip.sha256`。
8. 将这两个文件作为工作流产物上传，保留 14 天。

### 发布任务

发布任务在构建任务成功后运行于 `ubuntu-latest`，权限为 `contents: write`，使用 GitHub 自动提供的 `GITHUB_TOKEN`。它下载两个文件并用 `sha256sum --check --strict` 校验，然后以草稿形式创建 Release、上传附件并自动生成 Release notes，最后将草稿设为公开。标签带预发布后缀时，Release 标记为 prerelease。

使用该流程时需要注意：

- 已存在同名 Release 时，创建步骤会失败，流程不会覆盖已有 Release。
- 上传完成但公开步骤失败时，会留下一个未公开的草稿，需要手动处理。
- 由 `GITHUB_TOKEN` 推送的标签不会触发工作流，这是 GitHub 防止递归运行的机制。
- 工作流中使用的所有 Action 都固定到完整的提交 SHA。

## 代码结构

```
Core/            共享类型与数据契约
Protocol/        线格式解码与控制报文
Transport/       UDP 接收、对端锁定与来源校验
Mapping/         表情映射与视线解析
Processing/      表情后处理
Configuration/   配置模型、读取、校验与迁移
Runtime/         数据源选择、处理管线与诊断
Sdk/             VRCFT 模块入口
tools/           构建与打包脚本、配置工具
config/          默认配置样例
assets/branding/ 图标与标志
docs/            公开文档
```

`reference/` 是只读的第三方源码快照，已被 `.gitignore` 排除，不参与编译，也不会打包。

### 各层职责

| 目录 | 职责 |
|---|---|
| `Core` | 枚举（数据源、协议、连接状态、PICO 表情、模块输出通道等）、单调时钟、原始眼动与面部数据、数据源快照和模块输出模型。数据源快照为不可变对象 |
| `Protocol` | 四种线格式的解码器：`BridgeSplitDecoder`、`DaemonLegacyDecoder`、`Header16Decoder` 和 `BodyOnlyDecoder`，以及 Bridge 控制报文的生成与解析（`BridgeControl`） |
| `Transport` | `UdpTrackingSource` 负责套接字绑定、后台接收线程、时间戳去重、丢包计数和日志限流。`BridgeUdpSource` 实现 9030 端口的发现、心跳、通道订阅与对端锁定，`StreamingUdpSource` 实现 29765 和 29763 端口的接收与本机来源限制 |
| `Mapping` | `NeutralFaceMapper` 将 PICO 表情映射为模块输出通道，`EyeResolver` 由 `EyeLook` 形态推算每眼视线、由 `1 - EyeBlink` 得到睁眼度，并解析实测瞳孔直径 |
| `Processing` | `PostProcessor` 按固定顺序执行可选的后处理规则，并维护规则所需的时间状态 |
| `Configuration` | `ModuleConfig` 与 `PostProcessingSettings` 定义配置模型及默认值，`ConfigStore` 负责读取和写入，`ConfigValidator` 负责校验和回退，`LegacyImporter` 负责旧配置的只读迁移 |
| `Runtime` | `SourceCoordinator` 创建数据源并选定其中一个，`TrackingPipeline` 将选定的快照转换为输出并处理数据过期，`DiagnosticReporter` 每 5 秒输出状态日志 |
| `Sdk` | `UnifiedPicoTrackingModule` 继承 VRCFT 的 `ExtTrackingModule`，实现初始化、更新与清理 |

### SDK 边界

只有 `Sdk` 目录引用 VRCFaceTracking 和 `Microsoft.Extensions.Logging`，其他各层不引用任何 SDK 类型。各层之间通过 `Core` 中的类型交换数据，模块输出通道 `ExpressionChannel` 在 `Sdk` 层按名称转换为 VRCFT 的 `UnifiedExpressions`。

`UnifiedPicoTrackingModule.Write` 是唯一写入 `UnifiedTracking.Data` 的位置。它只写入模块已获得的输出：宿主未分配眼动输出，或配置中禁用了眼动时，不写入眼动通道和视线数据；面部同理。

### 运行时数据流

1. 每个数据源在独立的后台线程中接收 UDP 数据，解码并校验后，将结果发布为新的不可变快照。网络操作不在 VRCFT 的初始化或更新线程中执行。
2. VRCFT 每次调用 `Update` 时，`SourceCoordinator` 读取各数据源的最新快照并确定选用的数据源。
3. `TrackingPipeline` 依次完成过期判断、表情映射、视线解析和后处理，生成 `TrackingOutput`。
4. `Sdk` 层将输出写入 `UnifiedTracking`，并调用 `DiagnosticReporter` 输出状态日志。每次 `Update` 结束时休眠 2 ms。

## 默认配置

默认值定义在 `Configuration/ModuleConfig.cs` 和 `Configuration/PostProcessingSettings.cs` 中。`config/UnifiedPicoModule.example.json` 应与配置模型导出的结果保持一致。修改默认值后，可以使用配置工具导出新的默认配置并与样例对照：

```powershell
dotnet tools/ConfigTool/bin/Release/net10.0/UnifiedPicoConfigTool.dll defaults <输出路径.json>
```

该命令不会覆盖已有文件，输出路径需要是尚不存在的文件。

## PC舌头检查

构建固定SDK后运行`./tools/test-tongue.ps1`，使用临时环回socket测试生产接收器及SDK所有权，不安装或启动宿主。配套App与跨语言互通见[PC舌头开发说明](../TONGUE_zh.md)。

## 参与开发

参与方式请参阅[参与贡献](../CONTRIBUTING_zh.md)。构建通过只表示代码可以编译，与设备、发送端版本或 Avatar 的兼容性需要另行在真机上确认。
