# 参与贡献

[English](CONTRIBUTING.md) | 简体中文 · [README](README_zh.md) · [开发说明](docs/development_zh.md)

欢迎提交 Issue、Pull Request 或实机兼容性测试反馈。参与开发前，建议先阅读[开发说明](docs/development_zh.md)、[协议参考](docs/protocol_zh.md)和[隐私与网络安全](docs/privacy_zh.md)。

## 洁净室实现与第三方许可规则

> [!IMPORTANT]
> 严禁从没有明确开源许可证的仓库复制、翻译或改写代码与素材。

- **许可不明的上游仓库：** 在本项目核查的快照中，`PicoStreamingAssistantFTUDP`（SAFT）与 `lonelyicer/VRCFTPicoModule` 均无明确许可证。绝不可复制、移植或改写其中的实现代码。
- **基于协议资料的独立实现：** 参考公开线格式或行为观察实现功能时，须独立编写代码，并在 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 与 [THIRD_PARTY_NOTICES_zh.md](THIRD_PARTY_NOTICES_zh.md) 中记录参考地址、具体提交/版本与使用范围。
- **许可明确的内容复用：** 复用带有兼容许可证的代码或素材时，须记录来源、具体版本与文件范围，并完整保留原许可证与版权声明。

## 报告问题

提交 Bug 或实机兼容性结果时，请尽量提供：

- 复现步骤，以及实际表现与预期表现
- UnifiedPicoModule 版本与 VRCFT 版本（例如 `5.4.5` 或 Steam `6.0.0.0`）
- 发送端及版本（例如 `PicoFacialBridge 0.5.0`、`PICO Connect 10.6.6`）
- 头显型号（例如 `PICO 4 Pro`）
- VRCFT Output 页面中的相关日志（含每 5 秒的状态行与 `CONSUMPTION` 行）
- 涉及配置时：附上 `UnifiedPicoModule.json` 或 `UnifiedPicoConfigTool validate` 的输出

### 上传日志与配置前先脱敏

分享前请移除以下信息：

- 本机路径中的 Windows 用户名
- 头显与 PC 的局域网 IP 地址
- `UnifiedPicoModule.json` 中的 `Migration` 字段（记录有旧配置文件的绝对路径）
- 原始追踪数据与未经筛选的 `.pcap`/`.pcapng` 完整抓包（仅保留与问题相关的字段）

## 架构与编码要求

修改代码时请保持各层边界清晰（完整目录说明见[开发说明](docs/development_zh.md#代码架构)）：

- **SDK 隔离：** 只有 `Sdk/` 目录可引用 `VRCFaceTracking.*` 或 `Microsoft.Extensions.Logging`。`tools/ConfigTool` 仅编译 `Core/Contracts.cs` 与 `Configuration/*.cs`。
- **职责解耦：** 协议解码（`Protocol/`）、数据源选择（`Runtime/SourceCoordinator.cs`）、眼动解析（`Mapping/EyeResolver.cs`）与后处理（`Processing/PostProcessor.cs`）相互独立，修改其一不应波及其他部分。
- **以配置文件为唯一依据：** 不在代码中添加隐藏运行时预设，也不根据当前发送端自动改写用户配置文件。
- **零警告构建：** 代码风格与周围保持一致（项目启用了 `TreatWarningsAsErrors`）。注释仅用于说明代码本身不易看出的约束或协议边界。

### 配置字段变更

新增、重命名或修改配置字段、取值范围或默认值时，请同步完成以下各项：

1. 更新 `Configuration/` 中的模型与 `Configuration/ConfigValidator.cs` 校验逻辑。
2. 如影响旧配置迁移，更新 `Configuration/LegacyImporter.cs`。
3. 使用 `UnifiedPicoConfigTool.dll defaults` 重新导出 `config/UnifiedPicoModule.example.json`。
4. 同步更新中英文配置与后处理文档。
5. 若改变了已有字段的语义，在 PR 或提交说明中注明是否需要提升 `ConfigVersion`。

## 测试与验证

提交 PR 前请在本地执行 Release 构建与双宿主兼容测试：

```powershell
./tools/build.ps1 -Configuration Release
pwsh -NoProfile -File tools/test-compatibility.ps1
```

在 PR 或提交说明中：

- 如实写明实际执行过的验证（构建、兼容测试、真机测试）以及尚未验证的部分。编译通过不代表设备或 Avatar 兼容性已确认。
- 未经真机验证的发送端或版本组合须标注为未验证；未经真机标定的系数须标注为未标定，不要称为推荐值。当前已验证组合见[协议参考](docs/protocol_zh.md#验证状态)。

## Pull Request 检查清单

- [ ] 遵守洁净室规则；新增协议参考已登记至 `THIRD_PARTY_NOTICES*.md`
- [ ] `./tools/build.ps1 -Configuration Release` 零警告通过
- [ ] `pwsh -NoProfile -File tools/test-compatibility.ps1` 在 VRCFT 5.4.5 与 Steam VRCFT 6 下均通过
- [ ] 修改默认配置时已重新导出 `config/UnifiedPicoModule.example.json`
- [ ] 中英文文档已同步更新
- [ ] PR 说明中准确区分已验证与未验证范围
