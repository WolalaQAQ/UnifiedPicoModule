# 配置说明

[English](configuration.md) | **简体中文** · [返回 README](../README_zh.md)

本文说明 `UnifiedPicoModule.json`（`ConfigVersion: 1`）的全部字段、常用配置模板、校验回退规则以及旧配置迁移方式。14 条表情后处理规则的公式与调参细节请参阅[后处理参考](postprocessing_zh.md)。

## 文件路径与基本规则

无论 VRCFT 当前的工作目录在哪，模块都从 `UnifiedPicoModule.dll` 所在目录读取 `UnifiedPicoModule.json`：

```text
%APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442\UnifiedPicoModule.json
```

仓库中也提供了一份完整的默认配置模板：[`config/UnifiedPicoModule.example.json`](../config/UnifiedPicoModule.example.json)。

- **标准 JSON：** 不支持注释，字段名区分大小写。
- **枚举用字符串：** 例如 `"Source": "Bridge"`，不接受数字形式。
- **重启生效：** 配置只在模块初始化时读取，修改后需完全退出并重启 VRCFT。
- **不改写原文件：** 文件缺失时自动生成默认配置；已有文件存在语法错误或非法字段时，模块只在内存中回退并写日志，不改动磁盘上的原文件。

> [!TIP]
> 改完配置后，可以先用 `UnifiedPicoConfigTool validate UnifiedPicoModule.json` 离线检查一遍再重启 VRCFT。用法见[配置工具说明](../tools/ConfigTool/README_zh.md)。

## 常用配置模板

### 1. 固定头显 IP（装有 VPN、虚拟网卡或局域网有多台头显时推荐）

```json
{
  "ConfigVersion": 1,
  "Source": "Bridge",
  "Bridge": {
    "IP": "192.168.1.50"
  }
}
```

### 2. 仅 PICO 眼动追踪（例如 PICO 眼追 + Cymple 嘴捕混搭）

```json
{
  "ConfigVersion": 1,
  "Source": "Bridge",
  "DisableEyeTracking": false,
  "DisableFaceTracking": true
}
```

使用 `BridgeSplit` 时，模块只向头显订阅眼动通道，头显会同步停发嘴部数据；眨眼与眼周表情仍包含在眼动订阅内，闭眼追踪不受影响。

### 3. 数据超时后恢复中性姿态

```json
{
  "ConfigVersion": 1,
  "Stale": {
    "NeutralizeOnTimeout": true,
    "EyeMs": 250,
    "FaceMs": 250,
    "NeutralPupilMillimeters": 3.5
  }
}
```

### 完整默认配置

<details>
<summary>展开查看完整的默认 <code>UnifiedPicoModule.json</code>（ConfigVersion 1）</summary>

```json
{
  "ConfigVersion": 1,
  "Source": "Auto",
  "DisableEyeTracking": false,
  "DisableFaceTracking": false,
  "Bridge": {
    "IP": null,
    "ListenAddress": "0.0.0.0",
    "Protocol": null,
    "ReconnectMs": 2000,
    "ControlTimeoutMs": 30000
  },
  "Streaming": {
    "ListenAddress": "0.0.0.0",
    "LocalOnly": true,
    "IP": null,
    "Protocol": null,
    "SessionTimeoutMs": 2000,
    "HeaderProfile": null
  },
  "EyeGain": {
    "LeftX": 1,
    "LeftY": 1,
    "RightX": 1,
    "RightY": 1,
    "LeftOpenness": 1,
    "RightOpenness": 1
  },
  "Stale": {
    "NeutralizeOnTimeout": false,
    "EyeMs": 250,
    "FaceMs": 250,
    "NeutralPupilMillimeters": 3.5
  },
  "AllowFallback": false,
  "AutoSelectionWindowMs": 1500,
  "LegacyExpressionCeiling": false,
  "PostProcessing": {
    "ChannelGain": {
      "Enabled": true,
      "DefaultGain": 1,
      "Gains": {
        "EyeWideLeft": 2,
        "EyeWideRight": 2,
        "EyeSquintLeft": 1,
        "EyeSquintRight": 1,
        "JawOpen": 1
      }
    },
    "BrowInnerSeparation": { "Enabled": false, "Strength": 1 },
    "BrowDownResponse": { "Enabled": false, "Center": 0.7, "Slope": 10, "Gain": 1, "BlinkAsymmetryStrength": 0 },
    "SquintResponse": { "Enabled": false, "Gain": 1, "Gamma": 1, "BlinkInfluence": 0 },
    "FrownJawAttenuation": { "Enabled": false, "JawThreshold": 0.1, "TransitionWidth": 0.1, "MaxReduction": 0.5 },
    "ShrugToFrown": {
      "Enabled": false,
      "Threshold": 0.6,
      "Range": 0.2,
      "Gain": 1,
      "RollBlockThreshold": 0.3,
      "PuckerBlockThreshold": 0.4,
      "FunnelBlockThreshold": 0.1,
      "TransitionWidth": 0.05,
      "ConsumeShrug": false
    },
    "UpperUpPressGate": { "Enabled": false, "PressThreshold": 0.1, "TransitionWidth": 0.1, "MaxReduction": 1 },
    "PuckerFunnelAssist": { "Enabled": false, "PuckerThreshold": 0.3, "PressThreshold": 0.2, "TransitionWidth": 0.1, "Strength": 1 },
    "SmileRollSuppression": { "Enabled": false, "RollThreshold": 0.2, "TransitionWidth": 0.1, "MaxReduction": 1 },
    "StretchCompetition": { "Enabled": false, "StretchThreshold": 0.05, "SmileCeiling": 0.4, "TransitionWidth": 0.1, "SmileReduction": 0.5, "UpperUpReduction": 0.5 },
    "Symmetry": { "Enabled": false, "Groups": ["Smile"], "Method": "Max", "Strength": 1 },
    "CheekAsymmetry": { "Enabled": false, "MouthDifferenceThreshold": 0.1, "TransitionWidth": 0.1, "Gain": 1, "MouthSmoothingMs": 80 },
    "TongueHold": { "Enabled": false, "HoldMs": 120, "DropThreshold": 0.15 },
    "TongueJawGate": { "Enabled": false, "JawThreshold": 0.5, "TransitionWidth": 0.1, "MaxReduction": 1 }
  },
  "Migration": null
}
```

</details>

---

## 顶层字段

| 字段 | 类型 | 默认值 | 取值范围 | 说明 |
|---|---|---|---|---|
| `ConfigVersion` | 整数 | `1` | 仅 `1` | 配置格式版本；缺失、重复或不为 `1` 时，整份配置在内存中回退为默认值 |
| `Source` | 字符串枚举 | `"Auto"` | `"Auto"`、`"Bridge"`、`"Streaming"` | 决定启动哪些 UDP 接收器及选源方式 |
| `DisableEyeTracking` | 布尔 | `false` | `true` / `false` | 禁用眼动追踪输出 |
| `DisableFaceTracking` | 布尔 | `false` | `true` / `false` | 禁用面部表情输出 |
| `Bridge` | 对象 | 见 [Bridge](#bridge) | — | 端口 `9030` 接收设置（PicoFacialBridge / PicoFacialDataDaemon） |
| `Streaming` | 对象 | 见 [Streaming](#streaming) | — | 端口 `29765` 与 `29763` 接收设置（PICO Connect / Streaming Assistant） |
| `EyeGain` | 对象 | 各项均为 `1` | — | 左右眼视线与睁眼度倍率 |
| `Stale` | 对象 | 见 [Stale](#stale) | — | 过期时限、超时保持或中性化策略、VRCFT 5 中性瞳孔直径 |
| `AllowFallback` | 布尔 | `false` | `true` / `false` | 在 `Auto` 模式下，当前数据源不可用时是否整体切到另一数据源 |
| `AutoSelectionWindowMs` | 整数 | `1500` | `100–60000` | `Auto` 模式下启动后优先等待 Bridge 的时长（ms） |
| `LegacyExpressionCeiling` | 布尔 | `false` | `true` / `false` | 将表情输出上限设为 `0.99`（仅用于 SAFT 迁移近似，新安装无需开启） |
| `PostProcessing` | 对象 | 仅 `ChannelGain` 开启 | — | PC 端表情后处理规则，见 [PostProcessing](#postprocessing) |
| `Migration` | 对象或 `null` | `null` | — | `UnifiedPicoConfigTool` 写入的迁移来源记录，运行时不读取 |

### 数据源选择

| `Source` | 启动的接收器 | 选源行为 |
|---|---|---|
| `"Auto"` | Bridge + Streaming | 启动后前 `AutoSelectionWindowMs` 内只接受 Bridge；超时后谁先提供可用帧就锁定谁 |
| `"Bridge"` | 仅 Bridge（`9030`） | 固定使用 Bridge |
| `"Streaming"` | 仅 Streaming（`29765` / `29763`） | 立即固定使用 Streaming |

“可用数据”指在 `Stale` 时限内到达、且在当前开关下能实际写出数据的帧。心跳包、所有字段均无效的数据包或仅长度合法的包都不会触发选源。

数据源锁定后，本次运行期间不会因为另一端后来发数据而切换，也不会把两个来源拼在一起用（例如 Bridge 眼动 + PICO Connect 嘴部）。当 `Source` 为 `"Auto"` 且 `AllowFallback` 设为 `true` 时，如果当前来源失效而另一来源可用，模块才会整体切换过去。

### 眼动与面部开关

`DisableEyeTracking` 与 `DisableFaceTracking` 会与 VRCFT 分配给本模块的权限取交集。不属于本模块的通道不会被改写，方便与 Cymple 等其他模块混搭。若两项都为 `true`，模块不打开任何 UDP 端口。

使用 `BridgeSplit` 时，开关状态会通过 `PXR_SUB` 订阅请求发给头显，头显只发送被订阅的通道；其他协议只在本地过滤输出。

---

## `Bridge`

处理固定 UDP 端口 `9030` 上的 PicoFacialBridge（`BridgeSplit`）与 PicoFacialDataDaemon（`DaemonLegacy536`）。

| 字段 | 类型 | 默认值 | 取值与回退 | 说明 |
|---|---|---|---|---|
| `IP` | 字符串或 `null` | `null` | 单播 IPv4 字面量（不能为 `0.0.0.0`）；非法 → `null` | 头显 IP 地址；为 `null` 时启用局域网组播发现 |
| `ListenAddress` | 字符串 | `"0.0.0.0"` | 非组播、非广播 IPv4；非法 → `"0.0.0.0"` | 本地绑定的网卡地址 |
| `Protocol` | 字符串枚举或 `null` | `null` | `"BridgeSplit"`、`"DaemonLegacy536"` 或 `null`；非法 → `null` | 固定协议；`null` 表示由首个有效包自动识别 |
| `ReconnectMs` | 整数 | `2000` | `100–60000`；越界 → `2000` | 未连接时的发现重发间隔（ms），同时也是发现应答有效窗口（最短 1 秒） |
| `ControlTimeoutMs` | 整数 | `30000` | `1000–120000`；越界 → `30000` | 无任何控制包和数据包的超时（ms），超时后放弃当前对端并重新发现 |

- **自动发现（`IP: null`）：** 通过所有已启用、支持组播的以太网和 Wi-Fi 网卡向 `239.255.255.250:9030` 发送 `DISCOVER_DAEMON`，自动跳过名称含 `virtual`、`vpn`、`tunnel`、`wireguard`、`zerotier`、`tailscale`、`tap-`、`hyper-v`、`vmware`、`virtualbox` 的网卡；只接受与这些网卡同子网的应答（排除本机地址）。
- **指定 IP（填写 `IP`）：** 发现请求改为直接单播到该地址，只接受来自该地址 `9030` 端口的数据，不再做子网检查。
- **限定网卡（`ListenAddress`）：** 填具体本机 IPv4 地址时，自动发现也只走该网卡。
- **超时与降级：** 数据中断 250 ms 但控制心跳仍在到达时，状态变为 `MissingFrames` 而不踢掉对端；无活动满 `ControlTimeoutMs` 后才解除对端锁定（不发 `STOP`）并重新发现。若 `BridgeSplit` 的模式公告（`PXR_MODE`）在 `min(ControlTimeoutMs, 20000)` ms 内未刷新，已确认的增强能力会自动撤回。

---

## `Streaming`

处理固定 UDP 端口 `29765`（`StreamingHeader16`，如 PICO Connect）与 `29763`（`StreamingBodyOnly`，892 字节无头格式）。

| 字段 | 类型 | 默认值 | 取值与回退 | 说明 |
|---|---|---|---|---|
| `ListenAddress` | 字符串 | `"0.0.0.0"` | 同 `Bridge.ListenAddress` | 本地绑定的网卡地址 |
| `LocalOnly` | 布尔 | `true` | `true` / `false` | 只接受本机回环或已启用本机网卡 IPv4 发出的数据 |
| `IP` | 字符串或 `null` | `null` | 单播 IPv4 字面量（不能为 `0.0.0.0`）；非法 → `null` | 只接受该地址发出的数据 |
| `Protocol` | 字符串枚举或 `null` | `null` | `"StreamingHeader16"`、`"StreamingBodyOnly"` 或 `null` | 固定串流协议，并决定实际监听的端口 |
| `SessionTimeoutMs` | 整数 | `2000` | `100–60000`；越界 → `2000` | 未收到有效数据多久后结束会话并解除发送端与协议锁定（ms） |
| `HeaderProfile` | 对象或 `null` | `null` | 见 [`HeaderProfile`](#headerprofile) | 端口 `29765` 的可选严格 16 字节头校验 |

| `Streaming.Protocol` | 监听端口 | 接收条件 |
|---|---|---|
| `null` | `29765` 与 `29763` | 首个有效帧锁定协议，直到超过 `SessionTimeoutMs` |
| `"StreamingHeader16"` | `29765` | 长度 `>= 908` 字节且头部 `trackingType == 2`（读取 16 字节头后的 892 字节并忽略尾部多余字节；PICO Connect 10.6.6 实测为 912 字节） |
| `"StreamingBodyOnly"` | `29763` | 长度恰好为 `892` 字节 |

若 `LocalOnly` 为 `true` 而 `Streaming.IP` 填了非本机地址，串流接收器会启动失败并在日志报错。

### `HeaderProfile`

默认为 `null`，此时端口 `29765` 只校验长度（`>= 908` 字节）和 `trackingType == 2`，与 SAFT 及 pike 一致。如需严格校验 16 字节头部，必须显式给出全部六个字段（模块不内置魔数或版本默认值，也不重组分包）：

| 字段 | 类型 | 范围 |
|---|---|---|
| `StartCode1` | 整数 | `0–255` |
| `StartCode2` | 整数 | `0–255` |
| `SubType` | 整数 | `0–255` |
| `MultiPack` | 整数 | `0–255` |
| `CurrentIndex` | 整数 | `0–255` |
| `Version` | 整数 | `0–65535` |

任一字段缺失或越界时，整个 `HeaderProfile` 回退为 `null`（`29765` 回到默认宽松校验，`29763` 不受影响）。头部偏移定义见[协议参考](protocol_zh.md)。

---

## `EyeGain` 与眼动解析

所有发送端的视线和眼睑均来自表情模型，原生眼动包仅提供实测瞳孔直径：

- **视线（`blendshape-derived`）：** 左眼 X = `EyeLookInL - EyeLookOutL`，右眼 X = `EyeLookOutR - EyeLookInR`，双眼 Y = `EyeLookUp - EyeLookDown`。每只眼需要全部四个 `EyeLook` 形态；不含辐辏深度信息。
- **睁眼度（`blendshape-blink`）：** 每只眼取 `1 - EyeBlink`。VRCFT 按 `1 - Openness` 计算闭眼程度。
- **实测瞳孔：** 仅接受 `1–12` mm 范围内的有效测量值，失效时默认保持最近值。尚无测量或重置后，VRCFT 5 使用 `Stale.NeutralPupilMillimeters`（`3.5` mm），VRCFT 6 收到无测量标记 `0`。
- **独立 `X` 包：** 左右眼各自携带质量状态（`1` 可用、`-1` 未知但保留、`0` 不可用）。某眼状态为 `0` 时只保持该眼自己的独立历史值（尚无有效独立样本前保持中性），不影响另一只眼。

| 字段 | 类型 | 默认值 | 范围 | 说明 |
|---|---|---|---|---|
| `LeftX`、`LeftY`、`RightX`、`RightY` | 数值 | `1` | `0–10` | 乘在对应眼的视线轴上，随后限幅到 `[-1, 1]` |
| `LeftOpenness`、`RightOpenness` | 数值 | `1` | `0–10` | 乘在对应眼的睁眼度上，随后限幅到 `[0, 1]` |

越界或非有限数值回退为 `1`。倍率不影响瞳孔直径，且会与 VRCFT 自身的参数设置叠加。排查闭眼灵敏度时建议把 `LeftOpenness` 和 `RightOpenness` 保持在 `1`，因为大于 `1` 会在限幅时把轻度闭眼重新截成全睁。

---

## `Stale`

| 字段 | 类型 | 默认值 | 范围 | 说明 |
|---|---|---|---|---|
| `NeutralizeOnTimeout` | 布尔 | `false` | `true` / `false` | `false`：超时后保持最近的视线、眼睑、瞳孔与表情；`true`：超时后恢复中性值 |
| `EyeMs` | 整数 | `250` | `1–5000` | 原生眼动包实测瞳孔的有效时长（ms） |
| `FaceMs` | 整数 | `250` | `1–5000` | 表情数据（及由表情推算的视线 / 睁眼度）的有效时长（ms） |
| `NeutralPupilMillimeters` | 数值 | `3.5` | `1–10` | VRCFT 5 无实测瞳孔时的中性直径（mm）；VRCFT 6 始终收到 `0` |

所有数据年龄均按本机单调时钟的接收时间计算，不依赖发送端时间戳。

即使 `NeutralizeOnTimeout` 为 `false`，新鲜度时限依然用于 `Auto` 选源，并会在超期时重置后处理的时间状态（`CheekAsymmetry`、`TongueHold`）。新建会话、切换数据源或模式、模块停用及退出时始终会重置输出。日志里的 `MissingFrames` 固定在停帧 250 ms 后出现，不受 `Stale` 配置影响。

---

## `PostProcessing`

包含 14 条固定顺序执行的规则。配置中省略的规则保持默认状态，规则内省略的参数取默认值。

默认只开启 `ChannelGain`：

| 字段 | 类型 | 默认值 | 范围 | 说明 |
|---|---|---|---|---|
| `Enabled` | 布尔 | `true` | `true` / `false` | 是否启用按通道倍率 |
| `DefaultGain` | 数值 | `1` | `0–10` | `Gains` 中未列出的通道使用的倍率 |
| `Gains` | 对象 | `EyeWideLeft/Right: 2`；`EyeSquintLeft/Right`、`JawOpen: 1` | 每项 `0–10` | 按通道设置的倍率表（键名不区分大小写） |

在配置文件里写出 `Gains` 会整体替换默认表，未写出的通道（包括 `EyeWideLeft/Right`）全部改用 `DefaultGain`。若出现未知通道名、重复通道（不区分大小写）或越界数值，`ChannelGain` 整条规则会被禁用。

全部 14 条规则的参数、公式与可用通道名见[后处理参考](postprocessing_zh.md)。

---

## `Migration`

由 `UnifiedPicoConfigTool import` 写入，用于记录配置来源，运行时不读取。手动编写配置时保持 `null` 即可。

| 字段 | 类型 | 说明 |
|---|---|---|
| `SourceFile` | 字符串 | 旧配置文件的完整路径 |
| `SourceVersion` | 字符串 | 导入时指定的旧来源标签 |
| `ImportedAtUtc` | 字符串 | 导入时间（UTC ISO 8601） |
| `Differences` | 字符串数组 | 与迁移报告相同的差异条目 |

> [!WARNING]
> `SourceFile` 和 `Differences` 可能带有 Windows 用户名和局域网 IP 地址。分享配置前请检查，或直接将 `"Migration"` 改为 `null`。

---

## 校验与回退规则

模块逐字段校验，单个字段写错不会拖垮整份配置：

| 情况 | 处理方式 |
|---|---|
| 配置文件不存在 | 自动写入默认 `UnifiedPicoModule.json`（写入失败时使用内存默认值） |
| 文件不可读、JSON 语法错误或根节点不是对象 | 在内存中使用全部默认值，不改动磁盘原文件 |
| `ConfigVersion` 缺失、重复或不为 `1` | 在内存中使用全部默认值 |
| 未知字段或同一对象内的重复键 | 忽略该字段（保持默认）并记日志 |
| 类型错误（含整数形式的枚举、对象字段写成 `null`） | 忽略该字段（保持默认）并记日志 |
| 数值越界或 IPv4 地址非法 | 该字段回退为默认值并记日志 |
| `Streaming.HeaderProfile` 缺项或越界 | 整个 `HeaderProfile` 回退为 `null` |
| 某条后处理规则重复，或其参数未知 / 重复 / 类型错 / 越界 | **只禁用该规则**，其余规则正常生效 |
| `PostProcessing` 不是对象 | 关闭全部规则（包括 `ChannelGain`） |

IP 字段只接受 IPv4 字面量（不支持域名与 IPv6）。组播地址（`>= 224.0.0.0`）与 `255.255.255.255` 会被拒绝；`0.0.0.0` 仅允许用于 `ListenAddress`。

---

## 旧配置迁移概览

`UnifiedPicoConfigTool import` 支持从四种固定版本的旧配置生成新版 `UnifiedPicoModule.json` 和差异报告：

| 来源标签 | 对应旧文件 | 迁移后的主要设置 |
|---|---|---|
| `bridge-current-0.1-beta-split-fix1` | 分流改版 `PicoFacialDataModule.json` | `Source: "Bridge"`、`Bridge.Protocol: "BridgeSplit"`、`ShapeGain` → `ChannelGain` |
| `bridge-upstream-0ab66348` | 上游原版 `PicoFacialDataModule.json` | `Source: "Bridge"`、`Bridge.Protocol: "DaemonLegacy536"` |
| `saft-ab500f03` | SAFT `PicoModuleConfig.json` | `Source: "Streaming"`、`Streaming.Protocol: "StreamingHeader16"`、`LegacyExpressionCeiling: true`、`scales` → `ChannelGain` + `EyeGain` |
| `pike-c026aa4` | pike 分支 `config.ini` | `Source: "Auto"`、`eye_gain` → `EyeGain`、`mode` 展开为显式后处理规则 |

与全新安装的主要区别：

- 迁移**不会**额外加上新装默认的 EyeWide 2 倍补偿；只有旧配置存在有效且启用的增益设置时，迁移结果的 `ChannelGain` 才会开启。
- 旧版视线 / 睁眼度来源开关、`TrackingSettings` 瞳孔阈值和 `BridgeMode` 不导入，均记录在差异报告中。
- **pike `mode` 展开**（仅在导入时展开一次，运行时没有内置预设）：
  - `mode 0`：关闭全部后处理规则。
  - `mode 1`：开启 `ChannelGain`、`FrownJawAttenuation`、`UpperUpPressGate`、`PuckerFunnelAssist`、`SmileRollSuppression`、`TongueJawGate`（门控规则 `TransitionWidth: 0`）与 `Symmetry`（`["Smile"]`、`Max`）。通道增益：`JawOpen = 1/0.75`；`JawLeft/Right`、`MouthUpperLeft/Right`、`MouthLowerLeft/Right`、`MouthCornerPullLeft/Right`、`MouthCornerSlantLeft/Right = 2`；`MouthUpperUpLeft/Right = 1.25`。
  - `mode 2` *（缺少 `mode` 时的默认行为）*：在 `mode 1` 基础上，将 `Symmetry` 扩展为 `["Smile", "Frown", "Stretch", "UpperUp"]`，增加 `BrowInnerSeparation`（`Strength: 1`）、`ShrugToFrown`（`Range: 0.05`、`TransitionWidth: 0`）以及额外增益（`MouthClosed = 1/0.65`、`MouthStretchLeft/Right = 1/0.3`、`BrowInnerUpLeft/Right` 与 `BrowOuterUpLeft/Right = 2`）。`mode` 非法或重复时所有规则保持关闭。

命令语法、退出码与逐字段映射详见[配置工具说明](../tools/ConfigTool/README_zh.md)。
