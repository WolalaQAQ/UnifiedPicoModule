# PC舌头接收端：开发分支

[English](TONGUE.md) | 简体中文

本仓库与[PicoET-Enhance](https://github.com/WolalaQAQ/PicoET-Enhance/tree/feat/tongue-pc)配套使用`feat/tongue-pc`。[发送端说明](https://github.com/WolalaQAQ/PicoET-Enhance/blob/feat/tongue-pc/TONGUE_zh.md)包含相机源、PC App、模型与个人校准。模块只接数值结果，不运行模型或启动相机。

## 构建与使用

1. 按[开发说明](docs/development_zh.md)准备固定版本官方VRCFT SDK并构建，依赖仍为VRCFT 5.4.5／.NET 10。
2. 关闭VRCFT，按[安装说明](README_zh.md#安装)安装构建的模块，保留已有配置，不同时加载两个副本。
3. 测试PICO舌头时，由VRCFT将面部输出分配给PICO模块。PC App不选择或覆盖嘴部来源。
4. 在App中启用本机UDP输出到`127.0.0.1:45682`，再开始实时输入；端口与模块的`Tongue.Port`一致。

新增配置默认值：

```json
"Tongue": { "Enabled": true, "Port": 45682 }
```

`Port`范围1–65535，非法值回退45682。省略时采用默认值，不改写已有文件；`Enabled=false`关闭接收器。地址固定为IPv4环回。绑定失败会记录日志，正常PICO追踪继续。配置只在模块初始化时读取。

只有`ownsFace && Tongue.Enabled`才打开socket。Cymple嘴＋PICO眼混搭时，PICO模块不打开舌头端口、不写外部嘴部或舌头字段；宿主中保留原有眼嘴分工。

## 输出行为

| PC结果 | 模块输出 |
|---|---|
| 新鲜present | 使用PC的Out／Left／Right |
| 新鲜absent | 三项立即归零，即使原厂Out为正 |
| uncertain | 原正例最多保持100ms，同时受原期限限制 |
| invalid、stop、过期或缺失 | 恢复本轮原厂管线Out，清除Left／Right |

不做`max(native, model)`合并，不提供或写入Up／Down。原`LegacyExpressionCeiling`也作用于三项PC通道。主来源的保留末值设置不能关闭舌头过期。UDP线程只更新状态，原Write边界仍是唯一SDK写入者。暂停、Update失败及Teardown会取消并等待接收线程退出，再释放socket。

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

## 离线检查

先用`tools/build.ps1`准备`.sdk/`，然后执行：

```powershell
./tools/test-tongue.ps1 -ArtifactsPath artifacts/tongue-check
python tools/test-tongue-wire.py --dll artifacts/tongue-check/bin/TongueTests/release/TongueTests.dll --pc-wire ../PicoET-Enhance/pc-tongue/wire.py --output artifacts/tongue-wire
```

第一项覆盖生产解析／状态机、真实SDK Write所有权、环回UDP及生命周期；第二项使用配套App的真实Python编码器与隔离C# probe，也覆盖外部面部所有权。两者只用临时环回端口，不加载日常VRCFT。每轮互通选择新的输出目录。

这些检查不代表真人准确性或Avatar验收。实时追踪、校准、重训及原厂眼嘴并发需要配套发送端测试。

## 上游基础

参考[PR #1](https://github.com/WolalaQAQ/UnifiedPicoModule/pull/1)，审阅版本`f91d18d46e5661ea9e205eee9d854d965c4c99bc`，改为PC本机TJR1接收。未引入旧`Sdk/TongueReceiver.cs`、头显订阅或原厂／模型最大值合并；本分支不含无关兼容性／CI改动。
