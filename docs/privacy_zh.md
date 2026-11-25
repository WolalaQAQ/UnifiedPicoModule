# 隐私与网络安全

[English](privacy.md) | **简体中文** · [返回 README](../README_zh.md)

本文说明 UnifiedPicoModule 处理的数据范围、本地文件写入、日志内容以及局域网 UDP 通信边界。

## 处理的数据

模块从本地发送端接收视线、睁眼度、瞳孔直径（发送端提供时）和面部表情权重，**全程仅在内存中处理**，并写入 VRCFT 的 `UnifiedTracking.Data`。后续通过 OSC 发往 VRChat 的流程由 VRCFT 负责。

本模块**不会**：
- 将原始追踪帧、解码数值或抓包写入磁盘；
- 访问互联网（无遥测、无自动更新、无联网下载；模块图标直接嵌入 DLL 资源）；
- 收集账号、硬件标识或头显序列号。

每次 `BridgeSplit` 通道订阅会话都会临时生成随机的 16 位十六进制 ID（`Guid.NewGuid().ToString("N")[..16]`），与设备和用户无关。

## 本机文件

运行期间，模块只在**唯一一种情况**下写磁盘：模块目录不存在 `UnifiedPicoModule.json` 时，生成一份默认配置。已有的配置文件只读不写。

可能包含本机环境信息的文件：
- **`UnifiedPicoModule.json`：** 用户手动填写的 `Bridge.IP`、`Streaming.IP`、`ListenAddress`，以及 `UnifiedPicoConfigTool` 写入的 `Migration` 元数据。
- **`UnifiedPicoConfigTool import` 产物：** 只写入命令行显式指定的路径（绝不覆盖已有文件）。生成的配置与报告会原样记录旧配置完整路径（可能含 Windows 用户名）、输出路径、UTC 时间、旧配置 IP 以及未识别的旧字段原文。

## 日志内容

模块通过 VRCFT 的日志接口输出信息，自身不创建日志文件。

日志**绝不**打印视线、睁眼度、瞳孔或表情的具体数值，但可能包含：
- `%APPDATA%` 下 `UnifiedPicoModule.json` 的完整路径（含 Windows 用户名）；
- 未知或非法的配置字段，以及已启用后处理规则的生效参数；
- 监听地址 / 端口、已锁定对端的 IP:端口（例如 `Bridge peer locked: 192.168.1.20:9030`）和发送失败的目标地址；
- 每 5 秒一次的状态摘要（连接状态、会话号、接收频率、数据年龄、有效位、公告能力、丢包数、视线 / 睁眼 / 瞳孔来源以及内部耗时分布）；
- 异常类型与消息。

同类警告每 5 秒最多输出一次，且每个数据源的异步日志队列上限为 32 条，持续到达的畸形报文不会刷满 VRCFT 日志。

## 网络通信与端口

| UDP 端口 | 对应发送端 | 收发与控制报文 |
|---|---|---|
| `9030` | PicoFacialBridge / PicoFacialDataDaemon | 接收追踪帧；发送 `DISCOVER_DAEMON`（未指定 `Bridge.IP` 时在活跃的非 VPN 以太网 / Wi-Fi 网卡向 `239.255.255.250:9030` 发 TTL=1 组播，指定时单播）、`POLO` 心跳应答、`PXR_SUB` 通道订阅，以及停止时的 `STOP` |
| `29765` | PICO Connect（`StreamingHeader16`） | 接收追踪帧；对 `MARCO\0` 心跳回复 `POLO` |
| `29763` | 无头串流格式（`StreamingBodyOnly`） | 接收追踪帧；对 `MARCO\0` 心跳回复 `POLO` |

所有端口均以独占方式绑定（`ExclusiveAddressUse = true`），默认监听 `0.0.0.0`，可通过 `ListenAddress` 限定到单张网卡。将 `Source` 设为 `"Bridge"` 时不监听 `29765`/`29763`；设为 `"Streaming"` 时不监听 `9030`。

## 来源校验

UDP 不含加密或身份认证，局域网内来源 IP 可被伪造。模块通过结构和端点校验减少误接收，但无法抵御不可信局域网中的主动伪造：

- **通用校验：** 仅接受 IPv4 单播来源；按格式校验报文长度（见[协议参考](protocol_zh.md)），超过 `2048` 字节直接丢弃；会话首包必须含有效字段；时间戳重复或倒退直接丢弃；单次会话内锁定协议。
- **Bridge（`9030`）：** 来源端口必须为 `9030`。指定 `Bridge.IP` 时仅接受该地址；否则仅接受发现应答窗口（`max(1s, ReconnectMs)`）内来自同子网的响应（排除本机地址）。锁定对端后只接受该 IP:端口。`PXR_MODE` 必须是可打印 ASCII 且恰好含规定的 5 个字段；`PXR_SUB_ACK` 须匹配本次随机 ID、通道掩码不越界且 epoch 不回退；`BridgeSplit` 数据须匹配已确认的 epoch 与掩码。静默满 `ControlTimeoutMs`（默认 30 秒）后解除锁定。
- **Streaming（`29765` / `29763`）：** `Streaming.LocalOnly` 默认为 `true`（仅接受回环或本机活跃网卡 IPv4；若此时 `Streaming.IP` 填非本机地址会拒绝启动）。首帧锁定发送端 IP、端口与协议，无数据满 `SessionTimeoutMs`（默认 2 秒）后解锁。配置了 `Streaming.HeaderProfile` 时启用 16 字节头逐项匹配。

## 实用建议

- 仅在可信局域网中使用（不要在公共 Wi-Fi 上开启）。
- 不要在路由器上为 UDP `9030`、`29765`、`29763` 设置端口转发。
- 在 Windows 防火墙中仅在**专用网络**配置文件下放行 VRCFT。
- 使用本机运行的 PICO Connect 等串流软件时，保持 `Streaming.LocalOnly: true`。
- 局域网存在多台头显时填写 `Bridge.IP`；只用单一发送端时将 `Source` 固定为 `"Bridge"` 或 `"Streaming"`。

## 分享日志或配置前

- 隐藏局域网 IP 地址以及带有 Windows 用户名的文件路径。
- 精简或清空 `Migration` 字段（设为 `null`），反馈问题时只贴相关配置段。
- 不要上传原始抓包或追踪录像数据。

## 发布包清洁性

官方发布 ZIP 内附全新生成的默认 `UnifiedPicoModule.json`，不含个人 IP、迁移记录或本机路径。构建通过 `Directory.Build.props` 中的 `<PathMap>` 屏蔽编译机用户目录，`sdk-provenance.json` 只记录固定 SDK 提交、版本与文件摘要。
