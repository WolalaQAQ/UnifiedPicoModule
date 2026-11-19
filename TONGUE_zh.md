# PC端到端整嘴接收：开发分支

[English](TONGUE.md) | 简体中文

本仓库与[PicoET-Enhance](https://github.com/WolalaQAQ/PicoET-Enhance/tree/feat/tongue-pc)配套使用`feat/tongue-pc`。[发送端说明](https://github.com/WolalaQAQ/PicoET-Enhance/blob/feat/tongue-pc/TONGUE_zh.md)包含相机源、PC App、录制、回归模型与本地教师蒸馏。头显负责取图，PC负责配置和推理，本模块只接数值结果并在VRCFT原Write边界合并，不运行模型或启动相机。

## 构建与使用

1. 按[开发说明](docs/development_zh.md)准备固定版本官方VRCFT SDK并构建，依赖仍为VRCFT 5.4.5／.NET 10。
2. 关闭VRCFT，按[安装说明](README_zh.md#安装)安装构建的模块，保留已有配置，不同时加载两个副本。
3. 测试PICO整嘴回归时，由VRCFT将面部输出分配给PICO模块。PC App不选择或覆盖嘴部来源。
4. 在App中启用本机UDP输出到`127.0.0.1:45682`，再开始实时输入；端口与模块的`Tongue.Port`一致。

新增配置默认值：

```json
"Tongue": { "Enabled": true, "Port": 45682, "MouthMode": "Hybrid" }
```

`Port`范围1–65535，非法值回退45682。省略时采用默认值，不改写已有文件；`Enabled=false`关闭接收器。地址固定为IPv4环回。绑定失败会记录日志，正常PICO追踪继续。配置只在模块初始化时读取。

只有`ownsFace && Tongue.Enabled`才打开socket。Cymple嘴＋PICO眼混搭时，PICO模块不打开舌头端口、不写外部嘴部或舌头字段；宿主中保留原有眼嘴分工。

## MFR1整嘴输出

端到端模型使用256字节MFR1，schema为`pico-mouth50-v1`，通过同一个环回UDP端口发送50项命名连续控制量及有效位掩码。旧44字段原序保留，追加上下唇各自左右横移四项和上下Funnel两项。manifest显式`valid_mask`只能启用`trained_channels`中经过独立验证的`validated_channels`，零掩码候选保持禁用。

| `Tongue.MouthMode` | 整嘴输出 |
|---|---|
| `Hybrid`（默认，混合） | 每项优先使用实际支持、有效且新鲜的头显标量，0也是有效值；不足处才用模型有效新鲜值，两者都缺失则清除 |
| `ModelOnly`（完整模型） | 嘴部只使用模型；未知字段、释放、断流和过期均清除本模块拥有的嘴部字段，即使头显仍有数值 |

在模块JSON中修改这一个字段，重新初始化模块／重启VRCFT后生效。旧配置缺省为`Hybrid`，原文件不改写。ModelOnly在首个模型结果前或接收器绑定失败时同样清除嘴部，不回退头显。PC服务只发送模型结果，没有第二套模式选择。

原厂支持依据当前新鲜`RawFace`观测的面部有效位、字段存在性和有限`[0,1]`值，不依据管线保留值或后处理合成输出。CheekPuff、MouthLeft/Right、MouthFunnel的扇出复制不提供独立左右鼓腮、上下唇横移或上下Funnel观测，混合模式仍允许模型独立字段覆盖这些代理值。眼源保持原路径；Cymple等其他模块拥有嘴部时，两模式都不写也不清其字段。

MFR1不使用旧TJR的舌头出现阈值、方向缩放或`Out×direction`门控；0与小幅非零都作为模型控制值传递。序号、sensor时间、会话退役和来源年龄共同限制新鲜度，重复包不续期。解析与精确布局见[协议说明](docs/protocol_zh.md)。

配置段暂保留`Tongue`名称，以便已有配置继续使用同一个45682端口；这不表示新结果只包含舌头。接收器不引入ORT或教师依赖，也不修改官方VRCFT宿主。

## 旧TJR1／TJR2兼容输出

| PC结果 | 模块输出 |
|---|---|
| 新鲜present | 使用PC的Out／Left／Right，TJR2另提供Up／Down／Roll |
| 新鲜absent | 模型提供的舌头通道立即归零，即使原厂Out为正 |
| uncertain | 原正例最多保持100ms，同时受原期限限制 |
| invalid、stop、过期或缺失 | 恢复本轮原厂管线Out，清除Left／Right |

不做`max(native, model)`合并。只接过TJR1的实例不动Up／Down／Roll；一旦TJR2提供过这些通道，absent、invalid、过期、Teardown和切回TJR1都会清除扩展值，避免姿态残留。TJR2的Out为零时方向也归零。原`LegacyExpressionCeiling`作用于全部PC通道。主来源的保留末值设置不能关闭舌头过期。UDP线程只更新状态，原Write边界仍是唯一SDK写入者。暂停、Update失败及Teardown会取消并等待接收线程退出，再释放socket。

## TJR1线格式

每包恰好64字节，little-endian，无发现、订阅或回执。它不是旧头显UDP19031／TNG2协议，也不是`InputProtocol`主来源。

| 偏移 | 类型 | 字段 |
|---:|---|---|
| 0 | 4字节 | ASCII `TJR1` |
| 4／6 | 各uint16 | 版本1／长度64 |
| 8／16 | 各uint64 | 非零PC输出会话／源序号 |
| 24／32 | 各uint64 | 原嘴图sensor纳秒／PC发包单调纳秒 |
| 40／44／48 | 各float32 | Left／Right／Out，有限且在[0,1]内 |
| 52 | float32 | 发包时源年龄毫秒，有限且非负 |
| 56／60 | 各uint32 | 状态／保留零 |

状态只能是present=1、absent=2、uncertain=4、invalid=8或stop=16。普通结果要求seq与嘴图sensor时间递增；invalid／stop可用当前相同seq释放输出，不能用更旧seq。stop封闭会话，新源／模型／profile使用新输出会话，退役会话不得返回。

最长350ms期限扣除源年龄，源时钟锚进一步阻止积压包逐次续期。重复包、旧序号或重复sensor时间都不能延长输出期限。

### TJR2扩展

PC App的MouthLink模型在同一环回端口发送80字节TJR2。偏移0／4／6分别为`TJR2`／版本2／长度80，其余TJR1字段保持原偏移；末尾增加偏移64的float32 Up、68的Down、72的Roll和76的uint32保留零。扩展值必须有限且在[0,1]内。状态、源顺序、过期和会话退役规则与TJR1相同。TJR2需更新后的接收器，旧接收器会拒绝而不是误读。

MouthLink推理仍在PC App中使用既有ORT后端，模块不增加原生运行库依赖，不接管其余嘴型。这是数值互通入口，不包含已验收的个人模型。

## 离线检查

先用`tools/build.ps1`准备`.sdk/`，然后执行：

```powershell
./tools/test-tongue.ps1 -ArtifactsPath artifacts/tongue-check
python tools/test-tongue-wire.py --dll artifacts/tongue-check/bin/TongueTests/release/TongueTests.dll --pc-wire ../PicoET-Enhance/pc-studio/wire.py --output artifacts/tongue-wire
```

第一项覆盖生产解析／状态机、真实SDK Write所有权、环回UDP及生命周期；第二项使用配套App的真实Python编码器与隔离C# probe，也覆盖外部面部所有权。两者只用临时环回端口，不加载日常VRCFT。每轮互通选择新的输出目录。

这些检查不代表真人准确性或Avatar验收。实时追踪、校准、重训及原厂眼嘴并发需要配套发送端测试。

## 上游基础

参考[PR #1](https://github.com/WolalaQAQ/UnifiedPicoModule/pull/1)，审阅版本`f91d18d46e5661ea9e205eee9d854d965c4c99bc`，改为PC本机TJR1／TJR2接收。扩展舌头方向借鉴UndefinedSignal的[MouthLink贡献](https://github.com/WolalaQAQ/PicoET-Enhance/issues/5)。未引入旧`Sdk/TongueReceiver.cs`、头显订阅、模块内ORT或原厂／模型最大值合并；本分支不含无关兼容性／CI改动。
