# 配置说明

[English](configuration.md) | 简体中文 · [README](../README_zh.md)

本文说明 `UnifiedPicoModule.json`（`ConfigVersion` 为 1）的全部字段、取值范围、校验与回退规则，以及旧配置的迁移方式。后处理规则只在本文中概述，各规则的参数含义与公式请参阅[后处理参考](postprocessing_zh.md)。

## 配置文件

模块从 DLL 所在目录读取 `UnifiedPicoModule.json`，与进程的工作目录无关。通过 VRCFT 安装时，该目录为：

```
%APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442\
```

仓库中的 [`config/UnifiedPicoModule.example.json`](../config/UnifiedPicoModule.example.json) 是由配置模型导出的完整默认配置，可以复制到上述目录并改名为 `UnifiedPicoModule.json` 后编辑。

配置文件的基本规则如下：

- 文件为标准 JSON，不支持注释。字段名区分大小写。
- 枚举值以字符串书写，例如 `"Source": "Bridge"`，不接受整数形式。
- 配置只在模块初始化时读取。修改后需要重启 VRCFT，不支持热加载。
- 文件不存在时，模块会在该目录生成一份默认配置；如果无法写入，则使用内存中的默认值并在日志中说明原因。
- 模块不会改写已存在的配置文件。即使部分字段因非法而回退，原文件也保持不变。

个人配置和迁移报告可能包含 IP 地址或本机路径，分享前请参阅[隐私与网络安全](privacy_zh.md)。

## 顶层字段

| 字段 | 类型 | 默认值 | 取值 | 说明 |
|---|---|---|---|---|
| `ConfigVersion` | 整数 | `1` | 仅 `1` | 配置格式版本 |
| `Source` | 枚举 | `Auto` | `Auto`、`Bridge`、`Streaming` | 数据源选择方式，见[数据源选择](#数据源选择) |
| `DisableEyeTracking` | 布尔 | `false` | | 禁用眼动输出 |
| `DisableFaceTracking` | 布尔 | `false` | | 禁用面部表情输出 |
| `Bridge` | 对象 | 见 [Bridge](#bridge) | | PicoFacialBridge 与 PicoFacialDataDaemon 的接收设置（9030 端口） |
| `Streaming` | 对象 | 见 [Streaming](#streaming) | | 串流格式的接收设置（29765、29763 端口） |
| `Tongue` | 对象 | 见 [Tongue](#tongue) | | IPv4环回地址上的PC舌头结果 |
| `EyeGain` | 对象 | 各项均为 `1` | | 左右眼视线与睁眼度的增益 |
| `Stale` | 对象 | 见 [Stale](#stale) | | 数据过期判定与中性瞳孔值 |
| `AllowFallback` | 布尔 | `false` | | 当前数据源不可用时是否整体切换到另一数据源 |
| `AutoSelectionWindowMs` | 整数 | `1500` | 100–60000 | `Auto` 模式下启动后优先等待 Bridge 的时长（ms） |
| `LegacyExpressionCeiling` | 布尔 | `false` | | 将表情输出上限设为 0.99 |
| `PostProcessing` | 对象 | 仅 `ChannelGain` 启用 | | 表情后处理规则，见[后处理](#后处理) |
| `Migration` | 对象或 `null` | `null` | | 迁移工具写入的来源记录，运行时不使用 |

`ConfigVersion` 缺失、重复或不为 1 时，整份配置按默认值处理。数值超出范围的字段回退到该字段的默认值，并在日志中记录。

### 数据源选择

`Source` 决定启动哪些接收器：

| 取值 | 启动的接收器 | 选择方式 |
|---|---|---|
| `Auto` | Bridge 与 Streaming | 启动后 `AutoSelectionWindowMs` 内只接受 Bridge；此后采用最先提供可用数据的一方 |
| `Bridge` | 仅 Bridge | 固定使用 Bridge |
| `Streaming` | 仅 Streaming | 固定使用 Streaming，不等待 |

可用数据是指在 `Stale` 时限内到达、并且在当前输出开关下能实际输出的数据。控制心跳、所有字段都无效的数据包，以及仅长度符合要求的数据包，都不会使数据源被选中。

数据源一经选定，在本次运行期间保持不变，即使另一方随后开始发送数据也不会切换。模块不会同时使用两个数据源，例如眼动取自 Bridge、表情取自 Streaming。

`AllowFallback` 为 `true` 时，如果当前数据源不再可用而另一方可用，模块会整体切换到另一方。只有在 `Auto` 模式下两个接收器同时运行，因此该字段仅在 `Source` 为 `Auto` 时有实际作用。

### 眼动与面部开关

`DisableEyeTracking` 与 `DisableFaceTracking` 会与 VRCFT 提供的可用通道取交集：只有 VRCFT 允许且未被禁用的部分才由本模块输出，模块不会写入不属于自己的通道。两者都为 `true` 时，模块不启动任何接收器。

使用 BridgeSplit 时，模块会把开关状态作为订阅请求发给头显，头显只发送被订阅的通道。使用其他格式时，开关只影响本地输出，发送端仍会发送完整数据。

只开启眼动时，BridgeSplit 仍会发送 Blink 等眼周表情，闭眼追踪可以正常使用。

## Bridge

Bridge 接收器在 9030 端口接收 PicoFacialBridge（BridgeSplit）和 PicoFacialDataDaemon（DaemonLegacy536）的数据。端口固定，不能修改。

| 字段 | 类型 | 默认值 | 取值与回退 | 说明 |
|---|---|---|---|---|
| `IP` | 字符串或 `null` | `null` | 单播 IPv4 地址；非法时按 `null` 处理 | 头显地址。为 `null` 时自动发现 |
| `ListenAddress` | 字符串 | `0.0.0.0` | IPv4 地址，不能是组播或广播地址；非法时使用 `0.0.0.0` | 本地绑定地址 |
| `Protocol` | 枚举或 `null` | `null` | `BridgeSplit`、`DaemonLegacy536`；其他值按 `null` 处理 | 固定协议。为 `null` 时由首个有效数据包确定 |
| `ReconnectMs` | 整数 | `2000` | 100–60000，越界使用 2000 | 未连接时发送发现请求的间隔（ms） |
| `ControlTimeoutMs` | 整数 | `30000` | 1000–120000，越界使用 30000 | 对端既无控制包也无数据的超时（ms） |

`IP` 为 `null` 时，模块通过已启用、支持组播的以太网和 Wi-Fi 网卡向 `239.255.255.250:9030` 发送发现请求，并只接受与这些网卡处于同一子网的应答。名称中包含 VPN、虚拟机、隧道等关键字的网卡会被跳过。设置 `IP` 后，发现请求改为以单播发往该地址，且只接受来自该地址的数据，不再检查子网。PC 上有多张网卡、存在虚拟网卡或局域网内有多台头显时，建议设置 `IP`。

`ListenAddress` 为 `0.0.0.0` 时监听所有本地地址；设为某个本机地址时，自动发现也只通过该地址所在的网卡进行。绑定地址只决定接收位置，不能代替来源校验。

`ReconnectMs` 同时决定发现应答的有效窗口：发出发现请求后，在该时长（至少 1 秒）内收到的应答才会被接受。

`ControlTimeoutMs` 用于判断对端是否已离线。超时后，模块放弃当前对端并重新发现，不向对端发送停止指令。它与数据过期判定（`Stale`）无关：数据中断 250 ms 后连接状态即显示为 `MissingFrames`，但只要控制包仍在到达，连接就不会被放弃。BridgeSplit 的模式公告在 `ControlTimeoutMs` 与 20 秒中较短的时间内未刷新时，模块会撤销已确认的增强能力。这些超时默认保留输出；设置 `Stale.NeutralizeOnTimeout=true` 后才恢复中性。

`Protocol` 为 `BridgeSplit` 时，只接受与订阅确认相符的分流数据包；为 `DaemonLegacy536` 时，模块不发送订阅请求，只接受 536 字节数据包。各格式的结构请参阅[协议参考](protocol_zh.md)。

## Streaming

Streaming 接收器处理 PICO Connect 等发送端的串流格式。端口固定为 29765（带 16 字节头部的格式）和 29763（无头格式）。

| 字段 | 类型 | 默认值 | 取值与回退 | 说明 |
|---|---|---|---|---|
| `ListenAddress` | 字符串 | `0.0.0.0` | 同 `Bridge.ListenAddress` | 本地绑定地址 |
| `LocalOnly` | 布尔 | `true` | | 只接受本机发出的数据 |
| `IP` | 字符串或 `null` | `null` | 单播 IPv4 地址；非法时按 `null` 处理 | 只接受该地址发出的数据 |
| `Protocol` | 枚举或 `null` | `null` | `StreamingHeader16`、`StreamingBodyOnly`；其他值按 `null` 处理 | 固定协议，同时决定监听的端口 |
| `SessionTimeoutMs` | 整数 | `2000` | 100–60000，越界使用 2000 | 未收到有效数据多久后结束会话（ms） |
| `HeaderProfile` | 对象或 `null` | `null` | 见 [HeaderProfile](#headerprofile) | 29765 格式的严格头部校验 |

`Protocol` 与监听端口的关系如下：

| `Protocol` | 监听端口 | 接收条件 |
|---|---|---|
| `null` | 29765 与 29763 | 首个有效数据包决定协议，会话结束前不接受另一种协议 |
| `StreamingHeader16` | 29765 | 数据报不少于 908 字节，且头部 `trackingType` 为 2 |
| `StreamingBodyOnly` | 29763 | 数据报恰好为 892 字节 |

29765 格式的数据报由 16 字节头部和其后的 body 组成。模块读取头部之后的 892 字节，忽略多余的尾部字节；PICO Connect 10.6.6 实测发送 912 字节（16 + 896）。在未配置 `HeaderProfile` 时，模块不校验头部的起始码、子类型、分包与版本字段，接收判定与 SAFT 和 pike 分支一致。该格式中的有效位浮点不参与判定，表情是否有效由数值本身是否为有限值决定。

`LocalOnly` 为 `true` 时，只接受来自回环地址或本机已启用网卡 IPv4 地址的数据。PICO Connect 在本机运行，通常无需修改。若同时把 `IP` 设为非本机地址，串流接收器会启动失败并在日志中报错。

会话建立后，模块锁定首个有效发送端的地址与端口。超过 `SessionTimeoutMs` 未收到有效数据时，会话结束，发送端与协议的锁定一并解除，接收器回到等待状态。

### HeaderProfile

`HeaderProfile` 用于对 29765 格式启用严格的头部校验。默认值为 `null`，模块不内置任何起始码或版本值。

| 字段 | 类型 | 取值 |
|---|---|---|
| `StartCode1` | 整数 | 0–255 |
| `StartCode2` | 整数 | 0–255 |
| `SubType` | 整数 | 0–255 |
| `MultiPack` | 整数 | 0–255 |
| `CurrentIndex` | 整数 | 0–255 |
| `Version` | 整数 | 0–65535 |

六个字段必须全部提供。任一字段缺失或越界时，整个 `HeaderProfile` 按 `null` 处理并记录日志，接收回到上文的默认判定，29763 端口的接收不受影响。

配置完整的 `HeaderProfile` 后，29765 数据报除需满足长度与 `trackingType` 条件外，六个头部字段还必须与配置完全一致。模块不会重组分包数据。头部字段的偏移请参阅[协议参考](protocol_zh.md#streamingheader16-头部)。

## Tongue

`Enabled`默认`true`；`Port`默认`45682`，范围1–65535，非法值回退45682。绑定地址始终为`127.0.0.1`，只有本模块拥有面部输出权时才打开接收器。省略时采用默认值，不改写已有文件。PC舌头结果始终在最多350ms后过期，不受`Stale.NeutralizeOnTimeout`影响。详见[PC舌头说明](../TONGUE_zh.md)。

## 眼动解析与增益

视线与眼睑始终由表情模型推算，发送端的原生眼动包只用于读取实测瞳孔直径。

左眼 X 为 `EyeLookIn − EyeLookOut`，右眼 X 为 `EyeLookOut − EyeLookIn`，双眼 Y 均为 `EyeLookUp − EyeLookDown`，来源标记为 `blendshape-derived`。每只眼都需要自身的 `EyeLookIn`、`EyeLookOut`、`EyeLookUp`、`EyeLookDown` 四个形态；这只是由表情差值得到的近似，不包含辐辏信息。

睁眼度取本眼的 `1 - EyeBlink`，来源标记为 `blendshape-blink`。VRCFT 以 `EyeClosed = 1 − Openness` 计算闭眼程度。

缺帧、过期或无效时保持本眼最近有效值；尚无有效值前，视线回正、睁眼度为 1。可选的超时中性化由 [Stale](#stale) 设置。

独立表情 `X` 包为每只眼携带质量状态。状态为 0 时只保持本眼先前的输出；-1 表示未知，仍使用所提供的数据。某眼的首个 `X` 样本会替换此前保持的普通值，而不是继承它；此后该眼保持自己最近的有效值。

瞳孔直径来自原生眼动包：数据中有有效的实测值且在 1–12 mm 内时使用实测值，失效时默认保持。尚无实测值、输出重置后，或启用超时中性化且测量已过期时，使用 `Stale.NeutralPupilMillimeters`。模块不会根据睁眼度等其他数据估算瞳孔。

`Source=Auto` 时，原生眼动包含有效实测瞳孔，或形态数据可产生表情／可推算视线与睁眼度，都会使数据源被视为可用。

`EyeGain` 的六个字段分别作用于左右眼视线的 X、Y 轴和睁眼度：

| 字段 | 类型 | 默认值 | 取值 |
|---|---|---|---|
| `LeftX`、`LeftY`、`RightX`、`RightY` | 数值 | `1` | 0–10 |
| `LeftOpenness`、`RightOpenness` | 数值 | `1` | 0–10 |

超出范围或非有限值时回退为 1。增益相乘后，视线限制在 −1 到 1，睁眼度限制在 0 到 1。增益不作用于瞳孔直径，也可能与 VRCFT 自身的参数调整叠加。

`LeftOpenness` 与 `RightOpenness` 乘在睁眼度上。增益大于 1 时，限幅可能使轻微闭眼仍输出睁眼状态；排查闭眼响应时建议保持为 1。

## Stale

| 字段 | 类型 | 默认值 | 取值 | 说明 |
|---|---|---|---|---|
| `NeutralizeOnTimeout` | 布尔值 | `false` | `true` / `false` | 过期后是否输出中性值；关闭时保留最近输出 |
| `EyeMs` | 整数 | `250` | 1–5000 | 原生眼动数据中实测瞳孔的有效时长（ms） |
| `FaceMs` | 整数 | `250` | 1–5000 | 表情数据的有效时长（ms） |
| `NeutralPupilMillimeters` | 数值 | `3.5` | 1–10 | 无有效实测值时输出的瞳孔直径（mm） |

越界时回退到默认值。时长以本机收到数据的时间计算，不使用发送端时间戳。

`NeutralizeOnTimeout` 默认为 `false`，已有配置省略该字段时也保持关闭。视线、睁眼度和瞳孔测量过期后保留最近有效值，表情过期后保留最近输出。设为 `true` 时，表情超过 `FaceMs` 后归零；视线与睁眼度超过 `FaceMs` 后回正、睁眼度为 1；瞳孔测量超过 `EyeMs` 后改用 `NeutralPupilMillimeters`。

这些新鲜度时限仍用于选源和接纳输入，过期输入仍会重置后处理的时间状态。关闭中性化时，连接及模式公告超时保留输出；新会话、换来源或模式、停用及退出会重置输出。普通表情帧明确标为无效的区域会清零对应映射通道。独立 `X` 帧中，质量状态为 0 的眼则保持自身独立历史，并受超时中性化设置约束；本眼尚无有效独立样本时，表情通道为零、视线回正、睁眼度为 1。

日志中的 `MissingFrames` 状态固定在数据中断 250 ms 后出现，不受本节设置影响。

## 旧版兼容开关

| 字段 | 默认值 | 作用 |
|---|---|---|
| `LegacyExpressionCeiling` | `false` | 启用后，表情输出的上限为 0.99；关闭时为 1。不影响视线与睁眼度 |

该开关用于让迁移后的行为接近旧模块。迁移工具在 `saft-ab500f03` 下启用。新安装时无需启用。

## 后处理

`PostProcessing` 包含 14 条规则，每条规则是一个带 `Enabled` 字段的对象。规则按固定顺序执行，顺序不可配置。配置中省略的规则保持默认状态，规则内省略的参数使用默认值。

默认只启用 `ChannelGain`，其字段如下：

| 字段 | 类型 | 默认值 | 取值 | 说明 |
|---|---|---|---|---|
| `Enabled` | 布尔 | `true` | | 是否启用 |
| `DefaultGain` | 数值 | `1` | 0–10 | `Gains` 中未列出的通道使用的增益 |
| `Gains` | 对象 | `EyeWideLeft`、`EyeWideRight` 为 2；`EyeSquintLeft`、`EyeSquintRight`、`JawOpen` 为 1 | 每项 0–10 | 按通道设置的增益 |

在配置中写出 `Gains` 时，它会整体替换默认的通道列表，未列出的通道（包括 EyeWide）改用 `DefaultGain`。`Gains` 的键为表情通道名，匹配时不区分大小写；可用通道请参阅[后处理参考](postprocessing_zh.md#可用通道)。键无法识别、重复（不区分大小写）或数值越界时，`ChannelGain` 整条规则被禁用。禁用 `ChannelGain` 后，所有通道均按 1 输出，默认的 EyeWide 2 倍补偿也随之取消。

其余 13 条规则默认关闭。各规则的参数、范围与执行顺序请参阅[后处理参考](postprocessing_zh.md)。

## Migration

`Migration` 由迁移工具写入，用于记录配置的来源，运行时不读取。手动编写配置时保持 `null` 即可。

| 字段 | 类型 | 说明 |
|---|---|---|
| `SourceFile` | 字符串 | 旧配置文件的绝对路径 |
| `SourceVersion` | 字符串 | 迁移时指定的来源版本 |
| `ImportedAtUtc` | 字符串 | 迁移时间（UTC，ISO 8601） |
| `Differences` | 字符串数组 | 与迁移报告内容相同的差异记录 |

`SourceFile` 与 `Differences` 可能包含用户名、IP 地址等信息，分享配置前请检查或删除这部分内容。

## 校验与错误处理

模块按字段逐项校验，单个字段出错不会导致整份配置失效。

| 情况 | 处理方式 |
|---|---|
| 文件不存在 | 生成默认配置文件；无法写入时使用内存中的默认值 |
| 文件无法读取、不是合法 JSON 或根不是对象 | 整体使用默认值，原文件保持不变 |
| `ConfigVersion` 缺失、重复或不为 1 | 整体使用默认值 |
| 未知字段 | 忽略并记录日志 |
| 同一对象内的重复字段 | 不采用该字段 |
| 类型错误，包括未知枚举字符串、整数形式的枚举、对象字段为 `null` | 不采用该字段，保留默认值 |
| 数值越界或地址非法 | 回退到该字段的默认值并记录日志 |
| `HeaderProfile` 任一字段缺失或越界 | 整个 `HeaderProfile` 按 `null` 处理 |
| 某条后处理规则重复出现，或规则内有未知参数、重复参数、类型错误、越界值 | 仅禁用该规则，其他规则不受影响 |
| `PostProcessing` 中出现未知规则名 | 忽略并记录日志 |
| `PostProcessing` 不是对象 | 全部规则关闭，包括 `ChannelGain` |

IP 地址与绑定地址只接受 IPv4 字面量，不支持主机名和 IPv6。组播地址（首字节不小于 224）与 `255.255.255.255` 会被拒绝；`0.0.0.0` 只能用于绑定地址。

修改配置后，可以先用配置工具的 `validate` 命令离线检查，该命令只读取文件，不会创建或改写配置。用法请参阅[配置工具说明](../tools/ConfigTool/README_zh.md)。

## 旧配置迁移

使用 `UnifiedPicoConfigTool`，指定旧配置文件和来源版本，可以生成新配置与差异报告。核对报告后，将新配置复制为模块目录下的 `UnifiedPicoModule.json`，再重启 VRCFT。旧配置文件保留。

| 来源版本 | 旧配置文件 | 迁移后的主要设置 |
|---|---|---|
| `bridge-current-0.1-beta-split-fix1` | 分流改版 PicoFacialDataModule 的 `PicoFacialDataModule.json` | `Source=Bridge`、`Bridge.Protocol=BridgeSplit`；`ShapeGain` 转为 `ChannelGain` |
| `bridge-upstream-0ab66348` | 上游 PicoFacialDataModule 的 `PicoFacialDataModule.json` | `Source=Bridge`、`Bridge.Protocol=DaemonLegacy536` |
| `saft-ab500f03` | SAFT 的 `PicoModuleConfig.json` | `Source=Streaming`、`Streaming.Protocol=StreamingHeader16`、`LegacyExpressionCeiling=true`；`scales` 分别转为 `ChannelGain` 与 `EyeGain` |
| `pike-c026aa4` | pike 分支的 `config.ini` | `Source=Auto`；`eye_gain` 转为 `EyeGain`，`mode` 展开为后处理规则 |

两种 Bridge 来源都会迁移 `DisableEyeTracking`、`DisableFaceTracking` 和 `IP`；pike 的 `eye-tracking` 与 `expression-tracking` 对应这两个禁用开关。

旧配置中的视线与睁眼度来源选择不会导入；迁移结果使用[眼动解析与增益](#眼动解析与增益)所述的表情来源行为，差异记录在迁移报告中。

迁移结果与新安装的默认配置有以下区别：

- 迁移不会加入新安装时的 EyeWide 2 倍补偿。只有旧配置中的增益设置（`ShapeGain`、`scales` 或 pike 的 `mode`）有效且处于启用状态时，`ChannelGain` 才会启用，并使用迁移得到的增益。
- SAFT 迁移使用 `StreamingHeader16`，只监听 29765 端口；`HeaderProfile` 为 `null`。
- 旧 Bridge 配置中的 `TrackingSettings` 阈值与 `BridgeMode` 不会迁移，只记录在报告中。

pike 的 `mode` 按本项目的规则展开为显式参数，没有运行时预设：

| `mode` | 展开结果 |
|---|---|
| `0` | 不启用任何后处理规则 |
| `1` | 启用 `ChannelGain`、`FrownJawAttenuation`、`UpperUpPressGate`、`PuckerFunnelAssist`、`SmileRollSuppression`、`TongueJawGate`，以及 `Symmetry`（分组 `Smile`）；门控规则的 `TransitionWidth` 为 0 |
| `2` | 在 `mode 1` 的基础上，`Symmetry` 分组扩展为 `Smile`、`Frown`、`Stretch`、`UpperUp`，并启用 `BrowInnerSeparation` 与 `ShrugToFrown`（`Range` 为 0.05），另增加若干通道增益 |

`mode 1` 与 `mode 2` 的 `ChannelGain` 包括：`JawOpen` 为 1/0.75，`JawLeft`、`JawRight`、`MouthUpperLeft`、`MouthUpperRight`、`MouthLowerLeft`、`MouthLowerRight`、`MouthCornerPullLeft`、`MouthCornerPullRight`、`MouthCornerSlantLeft`、`MouthCornerSlantRight` 为 2，`MouthUpperUpLeft`、`MouthUpperUpRight` 为 1.25。`mode 2` 另加 `MouthClosed` 为 1/0.65，`MouthStretchLeft`、`MouthStretchRight` 为 1/0.3，`BrowInnerUpLeft`、`BrowInnerUpRight`、`BrowOuterUpLeft`、`BrowOuterUpRight` 为 2。`mode` 缺失时按 2 处理；取值非法或重复时，不启用任何后处理规则。

旧模块中无法对应的行为，例如 pike 的非线性眉部与眯眼曲线、鼓腮帧平滑，以及 SAFT 的视线与睁眼度 0.99 限幅，都会逐项写入报告。命令格式、退出码与输出文件规则请参阅[配置工具说明](../tools/ConfigTool/README_zh.md)。
