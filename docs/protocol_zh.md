# 协议参考

[English](protocol.md) | 简体中文 · [README](../README_zh.md)

本文档说明四种主要UDP追踪格式，以及模块与各发送端之间的连接行为。额外的环回TJR1舌头结果见[PC舌头协议](../TONGUE_zh.md#tjr1线格式)。内容以`Protocol/`、`Transport/`、`Mapping/`和`Runtime/`目录中的源码为准，这些源码不随模块ZIP分发。配置字段完整说明见[配置说明](configuration_zh.md)。

本模块独立实现上述格式。来源与许可请参阅[第三方声明](../THIRD_PARTY_NOTICES_zh.md)。

## 概览

| 格式（`InputProtocol`） | 发送端 | 本地端口 | 数据报长度 | 内容 |
|---|---|---:|---|---|
| `BridgeSplit` | PicoFacialBridge 0.3.0 及以上 | 9030 | 83、95、151、227；`X` v1 为 111/243，v2 为 131/263 | 19 字节公共头，后接已订阅通道；`X` 另带独立眼部数据 |
| `DaemonLegacy536` | PicoFacialDataDaemon | 9030 | 536 | 384 字节面部前缀与 152 字节眼动前缀 |
| `StreamingHeader16` | PICO Connect（legacy 面捕协议） | 29765 | 不少于 908 | 16 字节头与 892 字节 body，尾部多余字节忽略 |
| `StreamingBodyOnly` | 无头串流格式 | 29763 | 892 | 仅 body |

通用约定如下：

- 所有偏移均为从数据报起点计算的零基字节偏移。多字节整数按 little-endian 显式读取，浮点数为 little-endian IEEE 754 binary32，不使用本机结构体封送。
- 除 `StreamingHeader16` 的长度为下界外，其余格式的长度必须完全相等。
- 端口固定，不可配置。每个端口以排他方式绑定，若旧模块仍占用端口，绑定失败并在日志中报告端口与错误原因。
- 接收缓冲区为 2048 字节，大于所有支持的格式。

## 验证状态

独立表情 `X` v2 路径（PicoFacialBridge 0.5.0 搭配 PicoET-Enhance 2.3.0）已在 PICO 4 Pro 上、官方 VRCFT 5.4.5 下完成一次 84 秒的真人佩戴 Virtual Desktop 验证：Cymple 嘴部加 PICO 眼部，以及 PICO 眼部加面部。Bridge 每秒发出约 89.7 个 `X` 包，每个包都带新的眼部源。下面的 `X` v1 验收是更早的录制输入测试。

2026-10-01，`BridgeSplit` 在 PICO 4 Pro 上完成第一轮实机验收，内容包括发现、模式通告、订阅与确认、四种有效通道组合、epoch 一致性、切换时的缓存清理、数据中断后回到中性，以及应用重启后的恢复。同日，PICO Connect 10.6.6（`faceTrackingTransferProtocol=2`）的 29765 路径验收通过，实测数据报为 912 字节（16 字节头与 896 字节 body）。

头显完整重启后启动 Bridge 并重新连接 PC 也已验证。以下内容尚未验证：严格 `HeaderProfile` 模式、29763 路径、其他发送端与版本、`DaemonLegacy536`、PicoFacialBridge 普通模式，以及长时间运行的稳定性。

独立表情 `X` v1 扩展（PicoFacialBridge 0.4.0 搭配 PicoET-Enhance 2.2.0）已在官方 VRCFT 5.4.5 上验证：头显用事先录制的眼图回放原生眼部推理，PC 收到的 `E` 与双通道 `X` 包数值与源输出一致，视线与眼睑由表情推导，并保留逐眼各自的有效性。录制约为 23.7 Hz 并以加速循环回放，验证的是推理吞吐与传输，不是真实 90 Hz 相机序列。长时间实机相机使用及单眼质量失效（状态 0）尚未验证。

## Bridge 连接流程

`BridgeSplit` 与 `DaemonLegacy536` 共用 9030 端口和同一套发现与会话机制。Bridge 是单客户端会话，不是数据中继。

### 控制报文

控制报文为 ASCII，按字节精确匹配。除 `MARCO` 末尾的一个零字节外，均没有终止符。

| 报文 | 字节数 | 方向 | 作用 |
|---|---:|---|---|
| `DISCOVER_DAEMON` | 15 | PC → 头显 | 发现并申请会话 |
| `MARCO\0` | 6 | 头显 → PC | 心跳 |
| `POLO` | 4 | PC → 头显 | 心跳应答 |
| `PXR_MODE ...` | 10–256 | 头显 → PC | 模式通告，仅 BridgeSplit 发送端 |
| `PXR_SUB id=... mask=...` | 34 | PC → 头显 | 通道订阅 |
| `PXR_SUB_ACK id=... mask=... epoch=...` | 61 | 头显 → PC | 订阅确认 |
| `STOP` | 4 | PC → 头显 | 结束当前会话 |

### 发现

未锁定对端时，模块每隔 `Bridge.ReconnectMs`（默认 2000 ms）发送一次 `DISCOVER_DAEMON`：

- 未设置 `Bridge.IP` 时，向组播地址 `239.255.255.250:9030` 发送，TTL 为 1。模块在每个符合条件的网卡上各发一次。符合条件的网卡须处于活动状态、支持组播、类型为 Ethernet 或 Wi-Fi、具有非回环 IPv4 地址，且名称和描述中不含 virtual、vpn、tunnel、wireguard、zerotier、tailscale、tap-、hyper-v、vmware、virtualbox 等字样。`Bridge.ListenAddress` 不是 `0.0.0.0` 时，只使用该地址所在的网卡。
- 设置 `Bridge.IP` 时，以单播发送至该地址的 9030 端口，不再依赖网卡筛选。

没有符合条件的网卡时，日志会提示显式配置 `Bridge.IP`。

### 对端锁定

模块只接受源端口为 9030 的 IPv4 单播数据报。未锁定对端时，还需满足以下条件：

- 设置了 `Bridge.IP` 时，来源地址必须与之相同；
- 未设置时，来源地址必须与某个发现网卡位于同一子网，且不是该网卡自身的地址；
- 数据报在最近一次发现请求后的响应窗口内到达，窗口长度为 `max(1 s, ReconnectMs)`。

满足条件后，收到 `MARCO\0`、合法的 `PXR_MODE` 或可用的追踪数据时，模块锁定对端的完整 IP 与端口，此后只接受来自该端点的数据报。每次锁定都会生成新的 16 位十六进制订阅 id，状态变为 `MissingFrames`，直至收到数据。收到 `MARCO\0` 时模块回复 `POLO`。

### 模式通告

BridgeSplit 发送端在锁定后立即发送 `PXR_MODE`，之后每 10 秒及模式变化时重发：

```text
PXR_MODE mode=<normal|enhance> rooted=<0|1> enhance=<0|1> gate=<0|1> plugin=<off|left|right|dual>
```

尖括号部分为语法占位。接收端的解析规则如下：

- 报文只能包含可打印 ASCII 字符，长度为 10 至 256 字节，键值之间以空格分隔；
- 必须恰好包含 `mode`、`rooted`、`enhance`、`gate`、`plugin` 五个键，且不得重复；`mode` 只能为 `normal` 或 `enhance`，三个标志只能为 `0` 或 `1`；
- 只有在 `mode=enhance` 且三个标志均为 `1` 时，才认定处于增强模式；只有在增强模式下 `plugin=dual` 时，才认定支持分眼独立视线；
- 无法识别的 `plugin` 值按 `off` 处理，`left` 与 `right` 不代表分眼独立。

格式不合法的通告会被丢弃，并撤销此前记录的能力。能力变化时，模块推进 Generation 并清空已缓存的眼动与表情数据。若超过 `min(20000 ms, ControlTimeoutMs)` 未收到新的通告，能力降级为未知，日志中出现 `Mode advertisement expired; capabilities downgraded`。

已锁定为 `DaemonLegacy536` 时，收到的通告不会赋予任何能力。

### 订阅与确认

只有收到合法 `PXR_MODE` 后，模块才将对端视为 BridgeSplit 发送端并发送订阅。旧版 daemon 在心跳期间收到 `POLO` 以外的回复会判定心跳失败，因此模块不会向未发送模式通告的对端发送 `PXR_SUB`。`Bridge.Protocol` 固定为 `DaemonLegacy536` 时，模块也不发送订阅。

```text
PXR_SUB id=<16 位小写十六进制> mask=<请求通道>
PXR_SUB_ACK id=<相同 id> mask=<有效通道> epoch=<16 位小写十六进制>
```

通道掩码中，眼动为 1，面部为 2，两者均启用为 3。请求掩码取 VRCFT 宿主可用且配置未禁用的通道。两个通道均不可用时，模块不启动任何数据源。

在收到确认前，模块每秒重发一次订阅；收到每次模式通告时也会重发一次。订阅请求是幂等的，可补回丢失的请求或确认。

确认报文须满足以下全部条件，否则丢弃：

- 长度恰为 61 字节，字段顺序、单空格分隔与所示格式完全一致；
- 来自已锁定的对端，id 与当前订阅 id 相同；
- 有效掩码为 0 至 3，且是请求掩码的子集；
- epoch 为正的 signed int64；
- 已订阅时 epoch 不得小于当前值，epoch 不变时有效掩码不得改变。

有效掩码等于头显本地开关与 PC 请求的交集。确认中的 epoch 或掩码变化时，模块推进 Generation，清空眼动、表情和后处理的时间状态，并将协议锁定为 `BridgeSplit`。有效掩码为 0 时，头显暂停数据发送，心跳与模式通告继续，状态为 `MissingFrames`。

### 数据接收

确认之前，头显不发送追踪数据。BridgeSplit 数据报的 epoch 与有效掩码须与最近一次确认相同，否则丢弃，同时触发重发订阅。因此旧 epoch 的在途数据不会恢复已关闭的通道。

协议在会话内锁定：`Bridge.Protocol` 已指定时，不接受其他格式；首个通过校验的格式确定后，另一种格式的数据报将被丢弃，直至下次会话。

### 超时、重连与停止

| 情况 | 模块行为 |
|---|---|
| 超过 250 ms 未收到数据，但对端仍在发送控制报文 | 状态变为 `MissingFrames`，订阅与锁定保持不变 |
| 超过 `ControlTimeoutMs`（默认 30000 ms）未收到心跳、模式通告、确认或数据 | 不发送 `STOP`，解除锁定，清除订阅，状态变为 `Searching`，立即重新发现 |
| 已订阅时收到 epoch 倒退的确认，且超过 1 秒未接受数据 | 解除锁定，用新 id 重新发现 |
| 模块停止或被释放 | 仅向当前锁定的对端发送一次 `STOP` |

Bridge 应用重启后会丢失已锁定的端点，在收到新的发现请求之前不发送任何报文。PC 端只能等待 `ControlTimeoutMs` 到期后再重新发现，所以恢复时间主要取决于这一超时。2026-10-01 的应用重启测试中，头显界面约 6 秒回到运行状态，PC 端约 33 秒完成重新订阅。

epoch 基于头显的单调运行时间，整机重启后可能变小。正常的重启恢复流程会先经过超时和重新发现，届时已记录的 epoch 已被清除。已订阅会话内的 epoch 倒退按上表处理。

`STOP` 随停止流程发送。停止时若 200 ms 内无法获取状态锁，模块直接关闭 socket。

## BridgeSplit 包格式

### 公共头（19 字节）

| 偏移 | 大小 | 字段 |
|---:|---:|---|
| 0 | 1 | 标签：ASCII `E`、`A`、`F` 或 `X` |
| 1 | 1 | 有效通道掩码，取值 1 至 3 |
| 2 | 1 | 形态有效位：眼部为 1，面部为 2；须为掩码的子集 |
| 3 | 8 | epoch，正的 signed int64 |
| 11 | 8 | 源时间戳，正的 signed int64；`A`、`F`、`X` v1 使用面部源时间。`X` v2 使用左眼源时间，仅在该组未更新时允许为 0 |

### 数据报类型

| 标签 | 长度 | 有效掩码 | 内容 |
|---|---:|---|---|
| `E` | 83 | 含眼动（1 或 3） | 原生眼动；模块只输出其中的实测瞳孔直径 |
| `A` | 95 | 仅眼动（1） | 19 个眼周形态值 |
| `F` | 151 | 仅面部（2） | 33 个非眼周形态值 |
| `F` | 227 | 眼动与面部（3） | 全部 52 个形态值 |
| `X` | 111 | 仅眼动（1） | 与 `A` 相同的 19 个眼周形态，后接 16 字节逐眼扩展 |
| `X` | 243 | 眼动与面部（3） | 与 `F` 相同的 52 个形态，后接相同扩展 |
| `X` | 131 | 仅眼动（1） | 相同的 19 个形态，后接 36 字节 v2 扩展 |
| `X` | 263 | 眼动与面部（3） | 相同的 52 个形态，后接 36 字节 v2 扩展 |

各掩码下的发送组合为：仅眼动时发送 `E` 与 `A`；仅面部时只发送 `F`（151 字节）；两者均启用时发送 `E` 与 `F`（227 字节），不发送 `A`。

独立表情发送端用 `X` 替代 `A` 或 227 字节的 `F`。仅面部时仍发送 `F`，原生 `E` 包不变。

### `X`：独立眼部表情

掩码为 1 时，扩展从字节 95 开始；掩码为 3 时，从字节 227 开始：

| 相对偏移 | 大小 | 字段 |
|---:|---:|---|
| 0 | 4 | uint32 版本，1 或 2 |
| 4 | 4 | float32 已滤波右内眉；标准槽 3 为左内眉 |
| 8 | 4 | int32 左眼质量状态：-1 未知，0 不可用，1 可用 |
| 12 | 4 | int32 右眼质量状态，取值同上 |

v1 在这 16 字节后结束。v2 追加以下字段：

| 相对偏移 | 大小 | 字段 |
|---:|---:|---|
| 16 | 4 | uint32 更新组：bit 0 左眼、bit 1 右眼、bit 2 嘴 |
| 20 | 8 | signed int64 嘴源时间戳 |
| 28 | 8 | signed int64 右眼源时间戳 |

v2 的三个源时间戳均须非负，置更新位的组必须有正时间戳。掩码 1 只允许更新 bit 0、1；掩码 3 允许三个更新位。更新掩码为 0 时不携带新观测。

全部形态和右内眉必须为有限值。其他版本、状态、掩码或长度均拒绝。眼部形态按本侧状态判断可用性，不使用公共头中合并的眼有效位；-1 保留数据，不等于已确定的良好分类。非眼部形态仍由头部面部有效位控制。

`X` 的视线与眼睑和普通包一样由表情推算；质量状态决定每只眼的可用性。某眼不可用时，按配置的超时行为保持本眼最近输出，不借另一眼补齐。内眉分离按本侧处理，跳过跨眼 BlinkAsymmetry。

v1 共用一次时间戳和面部序列更新。v2 共用 epoch，但各更新组独立接纳：重复或倒退的源时间只拒绝本组。只有接纳的组推进序号、到达时间和后处理；眼快帧不刷新或重算缓存嘴部数据。新的不可用眼观测推进源身份，但不刷新最近可用输出的有效期；新的无效嘴观测仍交给处理层，使本组失效。分组与非分组表情之间切换时清空输入缓存和输出状态。两端须支持所用扩展版本，旧解码器会拒绝不支持的 `X` 包。

### `E`：原生眼动（83 字节）

| 偏移 | 大小 | 字段 |
|---:|---:|---|
| 19 / 23 / 27 | 各 4 | 左眼、右眼、合并视线的状态位，uint32 |
| 31 / 43 / 55 | 各 12 | 左眼、右眼、合并视线的方向向量，float32 × 3（X、Y、Z） |
| 67 / 71 | 各 4 | 左眼、右眼睁眼度 |
| 75 / 79 | 各 4 | 左眼、右眼瞳孔原始值 |

状态位只允许 `0x002`、`0x004`、`0x100`、`0x800`，出现其他位时整包丢弃。偏移 31 之后的所有浮点数必须为有限值。状态位的含义见[状态位与单位](#状态位与单位)。模块只消费该包中的瞳孔直径；视线与睁眼度来自表情形态。

普通模式下，Bridge 会清除每眼视线的有效位，只保留合并视线；仅在增强模式且 `plugin=dual` 时保留每眼视线。

### `A`：眼周形态（95 字节）

偏移 19 起为 19 个 float32，对应以下 PICO 形态槽：

| 槽 | 名称 | 槽 | 名称 |
|---:|---|---:|---|
| 0 | EyeLookDownL | 31 | EyeLookUpL |
| 2 | EyeLookInL | 35 | EyeLookUpR |
| 3 | BrowInnerUp | 36 | BrowOuterUpR |
| 4 | BrowDownR | 38 | EyeBlinkR |
| 11 | EyeLookInR | 41 | EyeSquintR |
| 12 | EyeLookDownR | 44 | EyeLookOutL |
| 16 | BrowDownL | 45 | EyeLookOutR |
| 26 | EyeSquintL | 46 | EyeWideR |
| 28 | EyeBlinkL | 47 | EyeWideL |
| 30 | BrowOuterUpL | | |

未列出的槽视为缺失。由于掩码为 1，面部有效位不可能置位。

### `F`：表情形态（151 或 227 字节）

151 字节版本自偏移 19 起为 33 个 float32，依次对应槽 1、5、6、7、8、9、10、13、14、15、17、18、19、20、21、22、23、24、25、27、29、32、33、34、37、39、40、42、43、48、49、50、51，即上表以外的全部槽位。眼周槽视为缺失。

227 字节版本自偏移 19 起为 52 个 float32，按 PICO 槽位顺序 0 至 51 排列。

52 个槽位的名称与顺序见 `Core/Contracts.cs` 中的 `PicoShape`。

### 校验与形态有效性

形态值中任一浮点数为非有限值时，整包丢弃。形态没有逐槽的存在标志：Bridge 发送前会将单个非有限值置 0，不清除整组有效位，因此接收端无法区分该槽的实测 0 与被置 0 的非有限值。

普通包的眼周形态（上表 19 个槽）受眼部有效位控制，其余槽受面部有效位控制；`X` 改用各眼自己的质量状态。即使面部无效，有效的眼周形态仍可单独使用。

## DaemonLegacy536

| 偏移 | 大小 | 字段 |
|---:|---:|---|
| 0 | 8 | 面部源时间戳，signed int64 |
| 8 | 288 | 72 个形态 float32；前 52 个参与映射，其余 20 个保留 |
| 296 / 300 | 各 4 | 眼部与面部有效性浮点，值恰为 `1.0` 时有效 |
| 380 | 4 | 填充，不是眼动时间戳 |
| 384 / 388 / 392 | 各 4 | 左眼、右眼、合并视线的状态位 |
| 432 / 444 / 456 | 各 12 | 左眼、右眼、合并视线的方向向量 |
| 468 / 472 | 各 4 | 左眼、右眼睁眼度 |
| 476 / 480 | 各 4 | 左眼、右眼瞳孔原始值 |

未列出的字段不参与映射。形态值为非有限值时，该槽视为缺失，不会丢弃整包。眼动部分没有可靠的源时间戳，按到达顺序处理。

该格式不提供分眼独立的能力声明。模块读取其中的实测瞳孔直径，视线与睁眼度来自表情形态。

## Streaming 格式

### Body 布局（892 字节）

| Body 偏移 | 大小 | 字段 |
|---:|---:|---|
| 0 | 8 | 面部源时间戳，signed int64 |
| 8 | 288 | 72 个形态 float32 |
| 296 | 40 | 10 个有效性浮点，前两个分别对应眼部与面部 |
| 336 | 4 | laughing |
| 340 | 40 | 10 个 emotion 浮点 |
| 380 | 512 | 保留 |

前 52 个形态参与映射，其余 20 个保存但不输出。有效性浮点、laughing、emotion 与保留区均不读取。形态值为有限值即视为存在，非有限值视为缺失。

模块不读取有效性浮点，是因为实测 PICO Connect 10.6.6 从不将其置为 `1.0`，而形态值随表情实时变化。SAFT 与 pike 同样不读取这些字段。

Streaming 格式不含原生眼动。视线与睁眼度由 `EyeLook` 与 `EyeBlink` 形态推算。

### StreamingHeader16 头部

| 偏移 | 大小 | 字段 |
|---:|---:|---|
| 0 / 1 | 各 1 | startCode1、startCode2 |
| 2 | 1 | trackingType，面部数据为 2 |
| 3 | 1 | subType |
| 4 / 5 | 各 1 | multiPack、currentIndex |
| 6 | 2 | version，uint16 |
| 8 | 8 | 传输时间戳，uint64 |
| 16 | 892 | Body |

Body 字段在数据报中的偏移相应增加 16：面部时间戳位于 16，形态位于 24。头部的传输时间戳不用作面部时钟。

PICO Connect 10.6.6 实测的头部前 8 字节为 `00 33 02 01 00 01 01 00`，其后为 8 字节时间戳。

### 接收规则

29765 端口（`StreamingHeader16`）：

- 默认模式下，数据报不少于 908 字节且 trackingType 为 2 即被接受，Body 取偏移 16 起的 892 字节，尾部多余字节忽略。其余五个头字段不做校验。这与 SAFT 和 pike 的判定一致。
- 设置 `Streaming.HeaderProfile` 后进入严格模式，在上述条件之外，还要求 startCode1、startCode2、subType、multiPack、currentIndex、version 六个字段与配置完全一致。六个字段须全部给出，前五个为 0 至 255 的整数，version 为 0 至 65535 的整数；任一字段缺失或越界时，整个配置回退为默认模式。严格模式不组装分片。

29763 端口（`StreamingBodyOnly`）：数据报必须恰为 892 字节。端口与布局取自 pike 与 lonelyicer 的实现，目前没有已确认的发送端。2026-10-02 曾将 PICO Connect 10.6.6 的 `faceTrackingTransferProtocol` 临时改为 1 并被动抓包，29763 与 29765 上均未观察到 UDP 流量。

两个端口共同的规则：

- 默认同时监听两个端口。`Streaming.Protocol` 指定格式后，只绑定对应端口。
- 只接受 IPv4 单播来源。`Streaming.LocalOnly` 默认为 `true`，此时来源必须是回环地址或本机活动网卡的 IPv4 地址。设置了 `Streaming.IP` 时，来源地址必须与之相同；`LocalOnly` 为 `true` 时该地址也必须是本机地址，否则启动时报错。
- 首个包含可用字段的数据报会锁定来源的 IP 与端口以及格式。全部字段无效的首包不会建立锁定。锁定后，结构合法但字段无效的新数据报仍会提交，使输出及时失效。
- 收到精确的 `MARCO\0` 时，模块向通过来源检查的发送端回复 `POLO`，但心跳本身不会锁定来源。
- 超过 250 ms 未收到数据时状态变为 `MissingFrames`。超过 `Streaming.SessionTimeoutMs`（默认 2000 ms）时清除锁定和缓存，状态变为 `Searching`，并重新读取本机地址。
- Streaming 发送端不支持远程停发，`DisableEyeTracking` 与 `DisableFaceTracking` 只影响本模块的输出。模块也不向 Streaming 发送端发送 `STOP`。

## 眼动解析

### 状态位与单位

适用于 `BridgeSplit` 的 `E` 数据报和 `DaemonLegacy536` 的眼动部分：

| 状态位 | 所在状态字 | 含义 | 附加条件 |
|---:|---|---|---|
| `0x002` | 合并视线 | 合并视线有效 | 向量三个分量均为有限值 |
| `0x004` | 每眼 | 睁眼度有效 | 有限值，且位于 0 至 1 |
| `0x100` | 每眼 | 每眼视线有效 | 向量三个分量均为有限值 |
| `0x800` | 每眼 | 瞳孔直径有效 | 原始值除以 10 换算为毫米，结果为正的有限值 |

原生方向向量的 X、Y 分量直接表示方向，不做归一化；模块不输出这些向量。

### 视线与睁眼度推算

视线始终由 `EyeLook` 形态推算，睁眼度始终取 `1 - EyeBlink`。推算视线时，该眼的 `EyeLookIn`、`EyeLookOut`、`EyeLookUp`、`EyeLookDown` 四个形态必须全部存在，来源标记为 `blendshape-derived`：

- 左眼 X = `EyeLookInL - EyeLookOutL`，右眼 X = `EyeLookOutR - EyeLookInR`；
- Y = `EyeLookUp - EyeLookDown`。

睁眼度取本眼的 `1 - EyeBlink`，来源标记为 `blendshape-blink`。VRCFT 以 `EyeClosed = 1 - Openness` 输出闭眼程度。

缺帧或无效的样本保持本眼最近有效值，不使用另一来源补齐；尚无有效值或启用超时中性化且已过期时，来源标记为 `neutral`。数据失效后的保持和输出见[时间戳、过期与中性输出](#时间戳过期与中性输出)。

结果乘以 `EyeGain` 中对应的增益后，视线限制在 -1 至 1，睁眼度限制在 0 至 1。

### 瞳孔

瞳孔直径只在状态位有效、换算结果位于 1 至 12 mm 内且所在数据包仍然新鲜时采用。失效时默认保持最近实测值。尚无实测值、输出重置后，或启用超时中性化且测量已过期时，输出 `Stale.NeutralPupilMillimeters`（默认 3.5 mm），日志中标记为 `estimated`。模块不从睁眼度或形态推算瞳孔。

## 时间戳、过期与中性输出

时间戳规则：

- signed int64 源时间戳为正时才视为可靠时钟。旧格式将 0 或负值按无时间戳处理，以到达顺序为准；BridgeSplit 必须使用正时间戳，只有 v2 未置更新位的组可使用 0。
- 在同一 Generation 内，原生眼动与表情分别检查时间戳。`A`、`F`、`X` v1 共用面部时间戳序列；`X` v2 分别维护左右眼与嘴的序列。重复或倒退的样本不推进本组到达时间、序号或处理状态。
- 新鲜度按本机单调时钟计算的到达时间判断，控制报文不会延长数据的新鲜度。

原生眼动与各表情组相互独立地到达、去重和过期，模块不做插值或合成。新的原生眼动样本不会触发对已缓存表情的重复处理。SDK 更新在没有新组观测时复用已处理输出；只有待到期的 `TongueHold` 可以在没有新嘴样本时改变嘴部输出。

`Stale.NeutralizeOnTimeout` 默认为 `false`：数据过期后，视线、睁眼度和瞳孔直径保持最近有效值，表情保持最近输出。新鲜度检查和后处理时间状态重置仍然生效。

设置 `Stale.NeutralizeOnTimeout=true` 时，过期后的输出为：

| 数据 | 过期阈值 | 过期后的输出 |
|---|---|---|
| 表情 | `Stale.FaceMs`，默认 250 ms | 全部通道归零，后处理的时间状态重置 |
| 视线与睁眼度 | `Stale.FaceMs`，默认 250 ms | 视线回正，睁眼度为 1 |
| 瞳孔 | `Stale.EyeMs`，默认 250 ms | `Stale.NeutralPupilMillimeters` |

没有选定数据源时，输出完全中性。新会话、换来源或模式时重置眼动解析与后处理状态。仅因连接或模式公告超时导致的 Generation 变化，在 `NeutralizeOnTimeout=false` 时保留眼动和表情输出，但仍清除传输缓存及后处理时间状态。停用、退出及更新异常会重置输出。

表情形态按固定表映射到 VRCFT 表情通道，取值限制在 0 至 1，映射过程不施加增益。映射之后的修正规则请参阅[后处理参考](postprocessing_zh.md)。`DisableEyeTracking` 与 `DisableFaceTracking` 分别屏蔽眼周形态与其余形态。

## SDK 消费与后台定时

`groupRxHzLRM` 表示接纳的左眼／右眼／嘴接收频率。`CONSUMPTION` 分开记录已处理组、被最新快照合并的中间观测，以及包含新组的 SDK 写入和当前输出的重复写入。耗时直方图覆盖接收到处理及 SDK 写入，不包括宿主 IPC、OSC 或 Avatar 渲染。

官方 VRCFT 5.4.5 在模块进程内请求 1 ms 定时精度。追踪期间，模块保存本进程 Windows timer 策略，仅关闭 `IGNORE_TIMER_RESOLUTION`，让这一请求在后台继续生效；停用、退出或失败时重新读取当前策略，只还原该位保存的控制／状态设置，其他位保留当前值。读取或写入失败时保留原快照，供后续重试。不需要窗口前台、宿主修改、优先级、CPU 节流或电源计划调整。后台唤醒更频繁可能增加功耗。

## 来源选择

`Source` 决定创建哪些数据源：`Bridge` 只创建 Bridge 数据源，`Streaming` 只创建 Streaming 数据源，`Auto` 同时创建两者。

状态为 `Faulted` 或 `Stopped` 的数据源不可用。其余数据源满足以下条件之一时视为可用：

- 未禁用眼动，原生眼动在有效期内，并含有有效的实测瞳孔；
- 存在新鲜的形态数据，并含有可映射的表情，或者在未禁用眼动时可用于推算睁眼度或视线。

`Auto` 模式下，启动后 `AutoSelectionWindowMs`（默认 1500 ms）内只选用 Bridge；超过该时限后，选用最先可用的数据源。Bridge 与 Streaming 同时可用时优先选用 Bridge。

选定后的数据源在本次运行期间保持不变。启用 `AllowFallback` 后，若当前数据源不可用而另一数据源可用，模块整体切换到另一数据源。模块不会同时使用两个数据源的数据。

## 网络安全

UDP 没有加密和身份验证。来源地址、端点锁定、长度与字段校验可以减少误接收，但无法防御可信网络内的伪造数据。请仅在可信网络中使用本模块，不要将 9030、29765、29763 端口暴露至公网。更多说明请参阅[隐私与网络安全](privacy_zh.md)。

## 来源证据

以下链接用于核对布局与行为，不代表复用其实现代码：

- thoricelli [PicoFacialDataModule](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/PicoFacialDataModule/PicoFacialDataModule.cs) 与 [PxrEyePoseDataV2](https://github.com/thoricelli/PicoFacialDataModule/blob/0ab66348bf970763322fe261b54445bcc5a2b23e/PicoFacialDataModule/Models/PxrEyePoseDataV2.cs)（`0ab6634`）：536 字节布局、眼动状态位与单位。
- regzo2 [SAFT LegacyConnector](https://github.com/regzo2/PicoStreamingAssistantFTUDP/blob/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7/PicoStreamingAssistantFTUDP/PicoStreamingAssistantFTUDP/PicoConnectors/LegacyConnector.cs)（`ab500f0`）：29765 的接收判定，仅检查 trackingType。
- pikepikeid [DataPacket](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/VRCFTPicoModule/Data/DataPacket.cs)、[LegacyDataPacket](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/VRCFTPicoModule/Data/LegacyDataPacket.cs) 与 [Updater](https://github.com/pikepikeid/VRCFTPicoModule/blob/c026aa455da60dd50492b3d36c9d360d0c343b8e/VRCFTPicoModule/Utils/Updater.cs)（`c026aa4`）：16 + 892 布局、长度下界与 trackingType 判定，以及 29763 端口。本模块的 29765 默认判定与其一致。
- WolalaQAQ [PicoFacialBridge TrackingData](https://github.com/WolalaQAQ/PicoFacialBridge/blob/836101c27feffccfbf7df6d94af9e98093f96057/shared/src/main/java/dev/pico/facialprobe/TrackingData.java) 与 [PeerProtocol](https://github.com/WolalaQAQ/PicoFacialBridge/blob/836101c27feffccfbf7df6d94af9e98093f96057/bridge/src/main/java/dev/pico/facialprobe/PeerProtocol.java)（`836101c`）：该版本仍发送 536 字节整包，可用于追溯控制报文与前缀来源，不能证明 BridgeSplit 或增强能力。
- BridgeSplit 的偏移、订阅控制、每眼有效位的清除规则与模式通告语义，取自 PicoFacialBridge 0.3.0 的 `TrackingData.java`、`TrackingMode.java`、`PeerProtocol.java` 与 `BridgeService.java`，并与其 README 中的线格式说明一致。
