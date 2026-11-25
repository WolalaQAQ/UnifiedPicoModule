# UnifiedPicoConfigTool

[English](README.md) | **简体中文** · [返回 README](../../README_zh.md)

`UnifiedPicoConfigTool` 是 UnifiedPicoModule 的离线命令行配置工具，支持校验新版 `UnifiedPicoModule.json`、导出干净的默认配置，以及迁移旧版 PICO 模块的配置文件。

工具**不依赖** VRCFaceTracking 或 VRCFT SDK，不会修改旧配置文件，不会覆盖已有输出路径，也不会自动安装或替换配置——请核对输出后再手动放入模块目录。

## 构建与快速使用

需要 **.NET 10 SDK**。在仓库根目录执行：

```powershell
dotnet build tools/ConfigTool/ConfigTool.csproj -c Release
$tool = "tools/ConfigTool/bin/Release/net10.0/UnifiedPicoConfigTool.dll"

# 查看命令语法与支持的旧配置来源标签
dotnet $tool --help

# 1. 只读校验现有新版配置
dotnet $tool validate UnifiedPicoModule.json

# 2. 导出默认配置作为对照
dotnet $tool defaults UnifiedPicoModule.defaults.json

# 3. 迁移旧配置，生成新配置与差异报告
dotnet $tool import legacy/PicoFacialDataModule.json bridge-upstream-0ab66348 UnifiedPicoModule.imported.json migration-report.txt

# 4. 校验迁移后的新配置
dotnet $tool validate UnifiedPicoModule.imported.json
```

构建后同目录下也会生成可直接运行的 `UnifiedPicoConfigTool.exe`。若已准备好固定版本的 VRCFT SDK 源码，执行 `./tools/build.ps1 -Configuration Release` 会一并构建模块与本工具（见[开发说明](../../docs/development_zh.md)）。

### 手动启用迁移后的配置

1. 打开 `migration-report.txt`，确认未导入字段与行为差异是否符合预期。
2. 彻底退出 VRCFT，先备份模块目录中现有的 `UnifiedPicoModule.json`。
3. 将确认好的新配置复制到以下路径并命名为 `UnifiedPicoModule.json`：
   ```text
   %APPDATA%\VRCFaceTracking\CustomLibs\8322e16d-c38b-42f0-8d35-68f27227b442\UnifiedPicoModule.json
   ```
4. 重新启动 VRCFT，在 **Output** 页面核对是否有配置告警。

---

## 命令与退出码

| 命令 | 参数 | 作用 |
|---|---|---|
| `validate` | `<配置文件.json>` | 使用与运行时完全一致的规则（`ConfigStore.ReadOnly`）做只读校验，不创建也不改写原文件 |
| `defaults` | `<输出文件.json>` | 将模型默认配置（`ConfigVersion: 1`，仅开启 `ChannelGain` EyeWide 2x）写入新文件 |
| `import` | `<旧配置文件> <来源版本> <新配置输出.json> <报告输出.txt>` | 读取单个旧配置文件，输出新版 JSON 与 UTF-8 迁移报告 |
| `--help` / `-h` | *（无）* | 打印命令语法与支持的 `<来源版本>` 标签 |

| 退出码 | `validate` | `defaults` | `import` |
|---|---|---|---|
| `0` | 无须人工确认的诊断 | 默认配置写入成功 | 新配置与报告均已写入（报告中的差异项不影响退出码） |
| `1` | 文件不存在 / 不可读、JSON 语法错误、根节点非对象或参数错 | 输出路径已存在、父目录不存在、写入失败或参数错 | 路径重复 / 已占用、未知来源标签、旧文件不可读或解析失败、写入失败或参数错 |
| `2` | 存在需人工确认的诊断（字段回退、规则禁用或未采用字段） | — | — |

### 命令细节

- **`validate`：** 以 `启用 ` 开头的输出行仅列出当前生效的后处理规则，属于正常信息输出（不会导致退出码 `2`）。与模块运行时不同，JSON 语法错误或根节点不是对象时会直接返回退出码 `1`，不做静默回退。
- **`defaults`：** 父目录必须已经存在；目标文件已存在时报错退出（返回 `1`）。
- **`import`：** 三个文件路径必须互不相同（不区分大小写），且两个输出路径都不能已存在。工具先写报告、再写新配置，完成后在报告末尾追加 `新配置成功写入：<路径>`。若中途失败，请检查指定路径后改用两个新的未占用路径重试。

---

## 支持的来源版本

`<来源版本>` 必须显式指定且区分大小写。由于上游原版与分流改版都叫 `PicoFacialDataModule.json` 但字段含义不同，工具不会根据文件名自动猜测。

| 来源版本标签 | 旧配置文件与固定参考提交 | 格式 |
|---|---|---|
| `bridge-current-0.1-beta-split-fix1` | 分流改版 `PicoFacialDataModule.json`（`0.1-beta-split-fix1`） | JSON |
| `bridge-upstream-0ab66348` | 上游原版 `PicoFacialDataModule.json`（[`0ab66348`](https://github.com/thoricelli/PicoFacialDataModule/tree/0ab66348bf970763322fe261b54445bcc5a2b23e)） | JSON |
| `saft-ab500f03` | SAFT `PicoModuleConfig.json`（[`ab500f03`](https://github.com/regzo2/PicoStreamingAssistantFTUDP/tree/ab500f0355cd8bbf8f9f5b4e6bcfe43d16499cb7)） | JSON |
| `pike-c026aa4` | pike 分支 `config.ini`（[`c026aa4`](https://github.com/pikepikeid/VRCFTPicoModule/tree/c026aa455da60dd50492b3d36c9d360d0c343b8e)） | `键: 值` 文本 |

---

## 字段映射说明

四种来源的共同规则：
- 迁移结果的 `ChannelGain` 初始为**关闭**（不自动套用新装默认的 EyeWide 2 倍补偿），仅当旧配置存在有效且启用的增益设置时才开启。
- 旧版视线 / 睁眼度来源开关不导入，统一采用表情推算视线与 `1 - EyeBlink`。
- 重复键、未知键与超出 `0–10` 的倍率均不采用并写入报告；写出前会再过一遍 `ConfigValidator`。

### PicoFacialDataModule（`bridge-current-*` / `bridge-upstream-*`）

- 固定设置：`Source: "Bridge"`、`LegacyExpressionCeiling: false`、`Bridge.Protocol` 为 `"BridgeSplit"`（`bridge-current`）或 `"DaemonLegacy536"`（`bridge-upstream`）。
- 迁移字段：`DisableEyeTracking`、`DisableFaceTracking`、`IP` → `Bridge.IP`（非有效 IPv4 时改为自动发现）。
- `ShapeGain`（仅 `bridge-current`）→ `PostProcessing.ChannelGain`（`Enabled`、`DefaultGain`、`Gains`；省略 `Gains` 时所有已映射通道写入 `1`，显式写 `{}` 则保留空对象并由 `DefaultGain` 生效）。任一条目非法时 `ChannelGain` 整体禁用。
- 不导入（记入报告）：`bridge-upstream` 中的 `ShapeGain`、`TrackingSettings.eyePupilDilationEyeOpennessThreshold`、`BridgeMode`。

### SAFT（`saft-ab500f03`）

- 固定设置：`Source: "Streaming"`、`Streaming.Protocol: "StreamingHeader16"`、`Streaming.HeaderProfile: null`、`LegacyExpressionCeiling: true`。
- `scales.EyeXGazeLeft/Right`、`EyeYGazeLeft/Right`、`EyeOpennessLeft/Right` → `EyeGain`（修正了旧版 SAFT 将 `EyeXGazeLeft` 误用于右眼 Y 轴的问题，正确读取 `EyeYGazeRight`）。
- `scales` 中其余有效表情键 → `ChannelGain.Gains`（`DefaultGain: 1`）。表情键出现未知、重复或非法值时 `ChannelGain` 整体关闭；眼动键非法时仅该项保持为 `1`。

### pike（`pike-c026aa4`）

按行解析 `键: 值`（键名不区分大小写，忽略空行与 `#` 注释，缺少冒号的行写入报告）。

- 固定设置：`Source: "Auto"`。
- `eye-tracking` / `expression-tracking`（`enable` / `disable`）→ `DisableEyeTracking` / `DisableFaceTracking`。
- `eye_gain: X,Y` → `EyeGain.LeftX/RightX = X`、`LeftY/RightY = Y`（格式不符时保持为 `1`）。
- `mode`（`0`、`1` 或 `2`；缺失按 `2` 处理；非法或重复时所有规则关闭）→ 导入时一次性展开为显式 `PostProcessing` 规则（运行时无内置预设）。各模式的具体展开参数见[旧配置迁移概览](../../docs/configuration_zh.md#旧配置迁移概览)。

---

## 迁移报告与隐私

迁移报告保存为独立 UTF-8 `.txt` 文件，同时镜像写入新配置的 `Migration.Differences`。内容包含旧文件路径、来源标签、UTC 时间、未导入 / 未知 / 重复字段、未复现的旧版行为差异以及校验修正项。

> [!WARNING]
> 报告和 `Migration` 字段会原样记录旧配置绝对路径（通常含 Windows 用户名）、旧配置 IP 地址和未知字段原文。分享前请脱敏检查，或将新配置中的 `"Migration"` 设为 `null`。

延伸阅读：[配置说明](../../docs/configuration_zh.md) · [后处理参考](../../docs/postprocessing_zh.md) · [隐私与网络安全](../../docs/privacy_zh.md) · [THIRD_PARTY_NOTICES_zh.md](../../THIRD_PARTY_NOTICES_zh.md)。
