# UnifiedPicoModule

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/branding/logo-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="assets/branding/logo-light.svg">
    <img src="assets/branding/module-icon.png" alt="UnifiedPicoModule" width="200" height="200">
  </picture>
</p>

[English](README.md) | 简体中文

UnifiedPicoModule 是面向 VRCFaceTracking（VRCFT）的 PICO 眼动与面部追踪模块。

PICO 头显的追踪数据可以通过多种发送端输出，包括 PicoFacialBridge、PICO Connect、Streaming Assistant 以及早期的 PicoFacialDataDaemon。这些发送端使用的数据格式和端口各不相同。本模块支持上述全部格式，能够自动识别当前可用的数据源，并将数据统一输出至 VRCFT，因此无需针对不同的发送端分别安装模块。

> 当前版本为 0.4.0，基于 VRCFT 5.4.5（net10.0）构建。各使用组合的验证情况请参阅[兼容性](#兼容性)，版本变化见[更新日志](CHANGELOG_zh.md)。

## 支持的发送端

| 发送端 | 端口 | 眼动 | 面部 |
|---|---|---|---|
| [PicoFacialBridge](https://github.com/WolalaQAQ/PicoFacialBridge) 0.3.0 及以上 | 9030 | 视线与眼睑由表情模型驱动；`E` 包提供实测瞳孔直径 | 支持 |
| PICO Connect（legacy 面捕协议） | 29765 | 视线与眼睑由表情模型驱动 | 支持 |
| 无头串流格式（如 Streaming Assistant） | 29763 | 视线与眼睑由表情模型驱动 | 支持 |
| PicoFacialDataDaemon（536 字节格式） | 9030 | 视线与眼睑由表情模型驱动；有效时提供实测瞳孔直径 | 支持 |

推荐使用 PicoFacialBridge，它提供实测瞳孔直径，并只发送 PC 端订阅的通道。逐眼独立表情（`X` 包）需要 PicoFacialBridge 0.4.0 及以上、头显端 [PicoET-Enhance](https://github.com/WolalaQAQ/PicoET-Enhance) 2.2.0 及以上，以及本模块 0.3.0 及以上；保持左眼、右眼和嘴各自时钟的高频 `X` v2 路径需要 PicoFacialBridge 0.5.0 及以上、PicoET-Enhance 2.3.0 及以上和本模块 0.4.0 及以上；普通模式不需要增强模块，接受 Bridge 0.3.0 及以上。如果已经在使用 PICO Connect 串流，本模块也可以直接接收 29765 端口的面捕数据，视线由 `EyeLook` 系列表情推算。

## 安装

### 前提条件

- Windows 10 或 Windows 11
- VRCFaceTracking 5.4.5
- 一种可用的发送端，且与 PC 处于同一局域网
- 支持面部追踪的 VRChat Avatar

### 安装步骤

1. **停用旧版 PICO 模块。** 包括 PicoFacialDataModule、PicoStreamingAssistantFTUDP（SAFT）以及 VRCFTPicoModule（lonelyicer / pike 分支）等。这些模块会占用 9030、29765、29763 端口，仅关闭追踪开关不一定能释放端口，建议在模块列表中将其停用或卸载。
2. **获取模块包。** 从 Releases 页面下载模块 ZIP，并使用随附的 `.sha256` 文件校验完整性。也可以按照[从源码构建](#从源码构建)自行生成模块包。
3. **安装模块。** 在 VRCFT 的 Module Registry 中选择 Install Module from .zip（不同版本的名称可能略有差异），选中模块包。安装完成后，完全退出并重新启动 VRCFT。
4. **启动发送端。** 在头显上启动发送端，确认头显与 PC 位于同一局域网。

安装成功后，模块列表中会显示 Unified PICO Face Tracking。“已初始化”仅表示模块加载成功，并不代表已经收到追踪数据。连接状态的确认方法请参阅[连接状态](#连接状态)。

## 配置

### 配置文件

配置文件名为 `UnifiedPicoModule.json`，与模块 DLL 位于同一目录：

```
%APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442\
```

首次启动时如果该文件不存在，模块会自动生成一份默认配置。使用时请注意以下几点：

- 修改配置后，需要重启 VRCFT 才能生效。目前不支持热加载。
- 配置文件为标准 JSON，不支持注释，字段名区分大小写。无法识别的字段或非法取值会被忽略，并记录在日志中，不会影响其余配置的加载。
- 更新或重新安装模块前，请先将配置文件备份到其他位置。VRCFT 在安装模块时可能会替换整个模块目录。

### 常用字段

| 字段 | 默认值 | 说明 |
|---|---|---|
| `Bridge.IP` | `null` | 指定头显的 IPv4 地址，适用于自动发现失败或局域网内存在多台头显的情况。该地址可以在 PicoFacialBridge 界面中查看 |
| `Source` | `Auto` | 数据源选择方式。可选 `Auto`、`Bridge` 或 `Streaming` |
| `DisableEyeTracking` / `DisableFaceTracking` | `false` | 分别禁用眼动或面部输出。使用 Bridge 时，头显会同步停止发送对应通道 |
| `EyeGain` | 均为 `1` | 分别设置左右眼视线 X、Y 轴与睁眼度的增益 |
| `AllowFallback` | `false` | 当前数据源不可用时，是否切换到另一数据源 |
| `Streaming.LocalOnly` | `true` | 仅接受本机发出的串流数据。PICO Connect 在本机运行，通常无需修改 |
| `Tongue.Enabled` / `Tongue.Port` | `true` / `45682` | 仅当本模块拥有面部输出权时，从IPv4环回地址接收PC舌头结果 |

完整的字段说明、取值范围和默认值请参阅[配置说明](docs/configuration_zh.md)。

`feat/tongue-pc`分支支持配套PC舌头App，构建、输出所有权、过期与离线检查见[PC舌头说明](TONGUE_zh.md)。不使用旧头显UDP19031协议。

只开启眼动时，Bridge 也会发送 Blink 数据，无需开启面部输出。

视线始终由本眼的 `EyeLookIn`、`EyeLookOut`、`EyeLookUp`、`EyeLookDown` 形态推算，睁眼度取 `1 - EyeBlink`。独立表情 `X` 包走同一路径，同时按左右分别保持有效性和内眉；所需的 Bridge 与 PicoET-Enhance 版本见[支持的发送端](#支持的发送端)。

独立眉值需要角色使用左右分开的眉参数。VRCFT 的合并参数 `v2/BrowInnerUp` 会对左右取平均，因此左=1、右=0时，位编码前的合并值为0.5。模块保留左右独立值，沿用官方 VRCFT 的参数规则。

使用配套 `X` v2 发送端时，左右眼与嘴的观测分别更新和过期；眼快帧保留嘴部最近结果，不重复处理。v1/v2 布局见[协议参考](docs/protocol_zh.md#x独立眼部表情)。

### 数据源选择

`Source` 为 `Auto` 时，模块会同时监听 Bridge 与串流端口。启动后的 1.5 秒内优先等待 Bridge 数据，超过这一时限后，采用最先提供有效数据的数据源。数据源一经选定，在本次运行期间保持不变，即使另一数据源随后开始发送数据，也不会切换过去。模块也不会同时使用两个数据源的数据，例如眼动取自一个来源、面部取自另一个来源。

因此，如果 PICO Connect 先于 Bridge 发出数据，模块会选定串流。需要使用 Bridge 时，请先启动 Bridge 再启动 VRCFT，或者将 `Source` 显式设置为 `Bridge`。

启用 `AllowFallback` 后，当前数据源不可用时，模块会整体切换到另一数据源。

## 连接状态

模块每 5 秒在 VRCFT 的 Output 页面输出一次状态日志。通过 PicoFacialBridge 正常连接时，日志示例如下：

```
Bridge/BridgeSplit: state=Connected ... selected=True E/morphology=90.0/24.0Hz ... enhance=True independent=True drops=0
Eye origin L/R=blendshape-derived/blendshape-derived; openness L/R=blendshape-blink/blendshape-blink; pupil L/R=measured/measured
```

主要字段的含义如下：

| 字段 | 含义 |
|---|---|
| `state` | `Connected`：已连接并在接收数据。`MissingFrames`：对端在线，但数据中断，例如头显休眠或发送端暂停。`Searching`：尚未发现对端 |
| `selected` | 该数据源是否为当前选用的数据源 |
| `E/morphology` | 眼动数据与表情数据的接收频率 |
| `groupRxHzLRM` | `X` v2 接纳的左眼／右眼／嘴频率；`CONSUMPTION` 另记处理和 SDK 写入，不代表 OSC 或 Avatar 刷新率 |
| `Eye origin` | 当前或保持中的视线来源。`blendshape-derived`：由 `EyeLook` 形态推算。`neutral`：无保留值，或启用超时中性化后数据已过期 |
| `openness` | 当前或保持中的睁眼度来源。`blendshape-blink`：由 `1 - EyeBlink` 得到。`neutral`：无保留值，或启用超时中性化后数据已过期 |
| `pupil` | `measured` 为实测瞳孔直径；`estimated` 表示使用默认值 3.5 mm |

使用 PICO Connect 时，日志中显示的数据源为 `Streaming/StreamingHeader16`；无论使用哪种发送端，视线来源均为 `blendshape-derived`。

VRCFT 无需保持窗口前台。追踪期间，模块让官方 VRCFT 的定时精度请求在后台继续生效；停止或失败时只还原 `IGNORE_TIMER_RESOLUTION` 原有的控制／状态位，保留进程策略其他位的当前值。

默认在数据过期后保留最近的视线、睁眼度、瞳孔直径和表情输出。设置 `Stale.NeutralizeOnTimeout=true` 可启用超时中性化：达到配置的过期时限后，视线回正、眼睛睁开、瞳孔改用中性值、表情归零。

## 表情后处理

后处理是位于数据解码与 VRCFT 输出之间的一个可选修正层。默认配置只启用 `ChannelGain` 一条规则，并且只将 EyeWide 左右通道放大 2 倍，用于补偿 PICO 瞪眼数值偏低的问题。其他通道保持原样输出。

除 `ChannelGain` 外，还有 13 条规则默认关闭。它们主要用于抑制 PICO 数据中常见的联动现象，例如张嘴时伴随出现撇嘴、抿嘴时上唇被带起、卷唇时残留笑容、舌头数值短时掉零等。

| 规则 | 作用 |
|---|---|
| `ChannelGain` | 按通道调整输出增益（默认启用） |
| `BrowInnerSeparation` | 抬内眉时减少外眉的联动 |
| `BrowDownResponse` | 调整皱眉的响应曲线 |
| `SquintResponse` | 调整眯眼的响应曲线 |
| `FrownJawAttenuation` | 张嘴时减少撇嘴的联动 |
| `ShrugToFrown` | 以下巴上抬（mouth shrug）辅助生成撇嘴 |
| `UpperUpPressGate` | 抿嘴时抑制上唇抬起 |
| `PuckerFunnelAssist` | 嘟嘴时辅助形成圆口 |
| `SmileRollSuppression` | 卷下唇时抑制笑容 |
| `StretchCompetition` | 横向咧嘴时抑制笑容与上唇抬起 |
| `Symmetry` | 使指定嘴部动作左右对称 |
| `CheekAsymmetry` | 根据嘴部左右偏移推测单侧鼓腮 |
| `TongueHold` | 舌头数值短时掉零时保持输出 |
| `TongueJawGate` | 嘴部张开幅度不足时抑制伸舌 |

上述规则的默认系数均未在真机上标定。每条规则在抑制误触发的同时，也可能削弱真实的复合表情，建议逐条启用并对照效果。如果只是整体幅度不足，建议优先调整 VRCFT 自身的参数。

启用规则时，只需修改该规则的 `Enabled` 字段，其余字段保持不变。例如：

```json
"TongueHold": { "Enabled": true, "HoldMs": 120, "DropThreshold": 0.15 }
```

规则按固定顺序执行，顺序不可调整。各规则的计算公式、参数范围以及组合使用时的注意事项，请参阅[后处理参考](docs/postprocessing_zh.md)。

## 配置迁移

配置工具 `UnifiedPicoConfigTool`（需从源码构建，不包含在模块包中）可以读取以下旧版配置：PicoFacialDataModule 的 `PicoFacialDataModule.json`、SAFT 的 `PicoModuleConfig.json`，以及 pike 分支的 `config.ini`。工具会据此生成新配置和差异报告。

工具不会自动应用生成的配置，请在核对差异后手动替换。迁移结果仅在行为上近似对应旧版，不会逐项复刻旧算法。用法请参阅[配置工具说明](tools/ConfigTool/README_zh.md)。

## 故障排查

**模块已初始化，但 Avatar 无响应**

请先查看日志中的 `state` 字段。如果始终为 `Searching`，常见原因有以下几种：

- 旧版模块仍占用端口；
- Windows 防火墙拦截了 VRCFT；
- PC 与头显不在同一网段。

自动发现会跳过 VPN 和虚拟网卡。如果 PC 上存在这类网卡，或者自动发现失败，请在 `Bridge.IP` 中指定头显地址。

**使用 Bridge，但日志显示选用了 Streaming**

这是因为串流数据先于 Bridge 到达，请参阅[数据源选择](#数据源选择)。

**修改配置后未生效**

请完全退出 VRCFT 后重新启动，并在日志中检查是否有关于未知字段或非法取值的提示。

**表情正常，但视线无响应**

请确认 `DisableEyeTracking` 未被启用。视线由发送端提供的 `EyeLook` 表情数据推算，其质量取决于发送端。

**伸舌时输出偶尔闪烁**

如果舌头数值存在短时掉零，可以启用 `TongueHold`。如果问题是嘴部未明显张开时就出现伸舌，应使用 `TongueJawGate`。两条规则针对的是不同的问题。

## 兼容性

已在 PICO 4 Pro 上验证的接入环境：

| 发送端 | 验证内容 |
|---|---|
| PicoFacialBridge 0.5.0 搭配 PicoET-Enhance 2.3.0（增强模式，`X` v2 包） | 在官方 VRCFT 5.4.5 上完成真人佩戴的 Virtual Desktop 串流验证：Cymple 嘴部加 PICO 眼部，以及仅 PICO 的眼部与眼面同开；Bridge 每秒约 89.7 个 `X` 包，每个包都带新的眼部源。到达 OSC 的速率取决于 VRCFT 宿主，见 [关于 90 Hz OSC](https://github.com/WolalaQAQ/PicoET-Enhance/blob/main/README_zh.md#关于-90-hz-osc) |
| PicoFacialBridge 0.4.0 搭配 PicoET-Enhance 2.2.0（增强模式，独立 `X` v1 包） | 在官方 VRCFT 5.4.5 上双通道接收，状态为 `Connected`、`enhance=True independent=True`；视线来自表情、眼睑来自 Blink、瞳孔为实测；通道订阅与眼动、面部开关 |
| PicoFacialBridge 0.3.0（BridgeSplit） | 通道订阅、眼动与面部开关、应用重启后恢复连接，以及 Avatar 表情跟随 |
| PICO Connect 10.6.6（`faceTrackingTransferProtocol=2`，29765 端口） | 面部数据约 72 Hz，无丢包，Avatar 跟随正常 |

`X` v1 路径使用事先录制的眼图，在头显上以约 23.7 Hz 的加速循环经原生眼部推理回放。`X` v2 路径做过一次 84 秒的真人佩戴串流验证。长时间使用和单眼质量失效（状态 0）尚未覆盖。其他接入环境的使用结果可通过 issue 反馈。

## 网络安全与隐私

本模块只在局域网内接收 UDP 数据，不访问互联网，不上传任何数据，也不将追踪数据写入磁盘。

UDP 本身没有加密和身份验证。模块会校验来源地址与数据包长度，这能够减少误接收，但无法防御有意伪造的数据。请仅在可信网络中使用本模块，不要将 9030、29765、29763 端口暴露至公网。详细说明请参阅[隐私与网络安全](docs/privacy_zh.md)。

## 从源码构建

构建需要 Windows、PowerShell、.NET 10 SDK 和 Git。模块需要基于固定版本的 VRCFaceTracking 源码进行编译：

```powershell
git clone https://github.com/benaclejames/VRCFaceTracking.git .sdk/source/VRCFaceTracking
git -C .sdk/source/VRCFaceTracking switch --detach 7add14873e398c4095d06913cdeee7a01fc43fab

./tools/build.ps1 -Configuration Release
./tools/package.ps1
```

构建脚本会校验 SDK 的提交版本与关键文件摘要。生成的模块包位于 `artifacts/` 目录，附带对应的 `.sha256` 校验文件。模块包不包含 VRCFaceTracking 的 DLL，这些运行依赖由宿主提供。如需指定已有的 SDK 源码目录或了解发布流程，请参阅[开发说明](docs/development_zh.md)。

## 相关文档

- [配置说明](docs/configuration_zh.md)
- [后处理参考](docs/postprocessing_zh.md)
- [协议参考](docs/protocol_zh.md)
- [隐私与网络安全](docs/privacy_zh.md)
- [开发说明](docs/development_zh.md)
- [配置工具说明](tools/ConfigTool/README_zh.md)
- [参与贡献](CONTRIBUTING_zh.md)

## 致谢

本模块参考以下项目公开的协议资料与行为观察，在此基础上独立实现：

- thoricelli：[PicoFacialDataModule](https://github.com/thoricelli/PicoFacialDataModule)、PicoFacialDataDaemon、[PICO-documentation](https://github.com/thoricelli/PICO-documentation)
- WolalaQAQ：[PicoFacialBridge](https://github.com/WolalaQAQ/PicoFacialBridge)
- regzo2：[PicoStreamingAssistantFTUDP](https://github.com/regzo2/PicoStreamingAssistantFTUDP)
- lonelyicer：[VRCFTPicoModule](https://github.com/lonelyicer/VRCFTPicoModule)
- pikepikeid：[VRCFTPicoModule](https://github.com/pikepikeid/VRCFTPicoModule)，多数后处理规则的设计参考
- benaclejames 及 [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) 贡献者
- crx：[Pico Wiki](https://pico.crx.moe/)

上述致谢仅说明参考来源，本项目未复制这些项目的代码。各来源的具体版本与许可边界请参阅 [THIRD_PARTY_NOTICES_zh.md](THIRD_PARTY_NOTICES_zh.md)。

## 许可证

本项目的原创代码与文档采用 [MIT](LICENSE) 许可证。本项目引用的 VRCFaceTracking SDK / Core 仍采用 Apache-2.0 许可证，且本项目不分发其 DLL。文中出现的 PICO、VRCFaceTracking 等名称仅用于说明互操作对象，并不代表获得官方认可或赞助。
