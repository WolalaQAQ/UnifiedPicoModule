# 宿主兼容测试套件

[English](README.md) | **简体中文** · [开发说明](../../docs/development_zh.md) · [README](../../README_zh.md)

本套件用于验证**同一份** `UnifiedPicoModule.dll`（基于 VRCFaceTracking 5.4.5 SDK 编译）能够在官方 VRCFT 5.4.5 与 Steam VRCFT 6（`6.0.0.0`）下正常加载并保持行为一致。

## 工作原理

`tools/test-compatibility.ps1` 会启动两个相互隔离的测试进程，分别链接到固定版本的 VRCFT 5.4.5（`vrcft5`）与 Steam VRCFT 6（`vrcft6`）。每个进程均满足以下隔离条件：

- 在 `artifacts/compat/<timestamp>/<host>/profile/` 下使用独立的 `APPDATA` 与 `LOCALAPPDATA` 目录。
- 初始化模块时关闭两个网络数据源，直接向正式代码路径的解码器、处理管线和 SDK 写入边界注入不可变 `SourceSnapshot` 或合成线格式字节。
- 验证 Steam 6 OSC 打包输出时，仅使用系统随机分配的本机回环 UDP 端口（`127.0.0.1:0`）。
- 不启动 GUI，不绑定正式追踪端口（`9030`、`29765`、`29763`），也不读取或改动现有 VRCFT 安装与用户配置。

## 测试覆盖范围

### 通用测试（VRCFT 5.4.5 与 Steam VRCFT 6）

- **生命周期与通道归属（`Program.cs`）：** 缺失配置文件时生成默认配置、幂等的 `Initialize`/`Teardown`、内嵌 PNG 图标可读性、分通道归属隔离（`ownsEye` / `ownsFace`）、暂停与异常回中性且不覆写他方通道，以及 `SourceCoordinator` 的粘性选择与 `AllowFallback`。
- **瞳孔来源编码（`Program.cs`）：** 两个宿主均透传实测瞳孔毫米值；无实测数据时，VRCFT 6（`NATIVE_HOST`）写入 `0`，VRCFT 5 写入 `Stale.NeutralPupilMillimeters`。
- **线格式解码与分组时钟（`InputTests.cs`）：** `BridgeSplitDecoder` 对 `F`、`X` v1 和 `X` v2（`263` 字节全通道包与畸形包拒绝）的校验、左眼/右眼/嘴独立时间戳去重、`groupUpdated` 掩码空操作与时钟保护、`EyeGain`/`ChannelGain` 不重复叠加、`TongueHold` 到期不重算嘴部、单眼质量失效（`state == 0`）与过期中性化，以及超时重置与显式会话重置的区别。
- **沙盒 IPC 与旧版 OSC（`Program.cs`、`LegacyTests.cs`）：** `ReplyUpdatePacket` 眼/嘴往返、旧版 `/avatar/parameters/v2/*` 公式（`JawOpen`、取平均的 `BrowInnerUp`）以及大端 OSC 浮点编码。

### Steam VRCFT 6 原生宿主测试（仅 `NATIVE_HOST`）

- **官方模块加载器（`ModuleLoaderTests.cs`）：** 通过 `VRCFaceTracking.ModuleProcess.ModuleAssembly` 在可回收 `AssemblyLoadContext` 中加载复制的 `UnifiedPicoModule.dll`，确认模块的 5.4.5 `VRCFaceTracking.Core` / `VRCFaceTracking.SDK` 引用绑定到宿主共享的 6.0 程序集而不私自重复加载，并验证非法路径与扩展名拦截。
- **102 字节原生面部 Blob（`NativeTests.cs`）：** 对照 `face-v1-keys.txt`（来源：[VRChat OSC 面部追踪规范](https://creators.vrchat.com/avatars/face-tracking/osc)，2026-11-05 核对）检验 `/tracking/face/v1` 132 字节 OSC 报文（`#bundle`、`,b`、102 字节大端 blob 及填充）、限幅与量化、10 项视线方向与眨眼、PC 后处理与独立内眉，以及队列中遇到已释放 `OscMessage` 时的容错。
- **Avatar 能力路由与瞳孔向量（`NativeTests.cs`）：** OSCQuery `avatarfullfacetracking` 在原生 `/tracking/face/v1` 与旧版 `/avatar/parameters/v2/*` 间切换、实测与无测量瞳孔序列化向量（`0` → 中性；`2.5`/`7.5` mm → 缩瞳/扩瞳），以及过期和退出时的保持/中性化行为。
- **混搭眼/嘴归属（`NativeTests.cs`）：** 使用合成数据源（非真实 Cymple 模块或 Avatar 渲染）经沙盒 IPC 分别运行仅眼与仅嘴实例，验证哨兵值保留，以及嘴部实例退出后眼部输出不受影响。

## 前置准备

在仓库根目录检出两份固定版本的 VRCFaceTracking 源码：

```powershell
# 官方 VRCFT 5.4.5（同时供 tools/build.ps1 使用）
git clone https://github.com/benaclejames/VRCFaceTracking.git .sdk/source/VRCFaceTracking
git -C .sdk/source/VRCFaceTracking switch --detach 7add14873e398c4095d06913cdeee7a01fc43fab

# Steam VRCFT 6.0.0.0（Build 26384174）
git clone https://github.com/benaclejames/VRCFaceTracking.git .sdk/compat/source/vrcft6-steam
git -C .sdk/compat/source/vrcft6-steam switch --detach 3d36379253d26961560d84acecd01d509aea467a
```

## 运行测试

在仓库根目录执行：

```powershell
# 先以 Release 配置构建模块，再运行两版宿主测试
pwsh -NoProfile -File tools/test-compatibility.ps1
```

### 可选参数

| 参数 | 默认值 | 说明 |
|---|---|---|
| `-ModuleDll` | *（先构建 Release）* | 直接测试已有的 `UnifiedPicoModule.dll`，跳过编译 |
| `-LegacySdkSource` | `.sdk/source/VRCFaceTracking` | 固定 5.4.5 源码路径（`7add14873e398c4095d06913cdeee7a01fc43fab`） |
| `-NativeSdkSource` | `.sdk/compat/source/vrcft6-steam` | 固定 Steam 6 源码路径（`3d36379253d26961560d84acecd01d509aea467a`） |
| `-InstalledHostDir` | *（无）* | 可选的本地 Steam VRCFT 6 安装目录（须匹配提交 `3d363792...`）；仅复制 `VRCFaceTracking.{Core,SDK,ModuleProcess}.dll` 和 `fti_osc.dll` 到隔离测试输出目录，不改动原安装 |

测试已编译 DLL 的示例：

```powershell
pwsh -NoProfile -File tools/test-compatibility.ps1 -ModuleDll bin/Release/net10.0/UnifiedPicoModule.dll
```

## 输出产物与 `KNOWN-UPSTREAM` 用例

每次运行都会在 `artifacts/compat/<yyyyMMdd-HHmmss-fff>/` 下写入完整记录：

- `summary.json` —— 模块路径、可选宿主安装路径及各宿主报告索引
- `vrcft5/results.json`、`vrcft6/results.json` —— 通过/失败列表、宿主程序集版本与 `knownUpstream` 说明
- `vrcft5/tests.log`、`vrcft6/tests.log`（及 `build.log`）—— 完整测试输出日志

套件中包含两项 `KNOWN-UPSTREAM` 探针，用于记录上游原版宿主的行为边界而非本模块缺陷：

1. **`BrowOuterUpLeft` IPC 复制区间重叠：** 固定版本 5.4.5 与 Steam 6 均将 `BrowOuterUpLeft` 同时划入眼部与嘴部 IPC 复制范围，因此非哨兵的嘴部 IPC 回复可能按回复顺序覆写该通道。
2. **`BinaryFaceDataSender` 订阅生命周期：** Steam 6 原版发送器在能力事件到达前即处于已订阅状态，且后续能力事件会重新订阅已停止的实例。

这些探针通过仅表明模块在不修改 `VRCFaceTracking.Core` 的前提下兼容原版宿主行为，并不代表上述上游行为已经修复。
