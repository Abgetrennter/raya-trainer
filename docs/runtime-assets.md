# 运行时资产包（Runtime Asset Bundle）

RayaTrainer 的部分功能（选中单位倍率、全军链式修改器、娱乐生成物等）通过一个**预编译资产包**实现：包内是标准的 Sage 引擎资产流（`mod.manifest` / `mod.bin` / `mod.relo` / `mod.imp`），由注入的 Agent 在游戏进程内经引擎自身的资产加载函数挂载，进程退出即消失，**不修改、不依赖游戏目录中的任何文件**。

## 源与产物

本仓库**只提交文本真相**；四个（双格式共八个）二进制资产流是本地生成产物，不入库：

| 内容 | 位置 | 是否入库 |
|---|---|---|
| 资产声明源 | `src/RayaTrainer.Core/RuntimeAssets/AttributeModifiers/Mod.xml.source` | ✅ |
| 哈希合同 + 模板表 | `src/RayaTrainer.Core/RuntimeAssets/AttributeModifiers/asset-manifest.json` | ✅ |
| JSON Schema | `asset-manifest.schema.json` | ✅ |
| 生成脚本 | `scripts/build-runtime-assets.ps1` | ✅ |
| Game 6 四流（RA3 1.12） | `AttributeModifiers/mod.*` | ❌ 本地生成 |
| Game 7 四流（Uprising） | `AttributeModifiers/uprising/mod.*` | ❌ 本地生成 |

生成脚本对同一输入**确定性**地产出逐字节一致的流，并把构建机的临时路径等溯源信息规范化为常量，因此任何机器重建的结果都相同。

RA3 1.12 的 XML 源保留 `Worldbuilder.xml` Reference 作为**仅构建期依赖**，供 BAB 与
ModAssetResolver 解析只存在于 WorldBuilder 流中的美术。所需美术连同 Raya-owned ID 在最终
四流中物化后，生成脚本会从最终 `mod.manifest` 删除 `worldbuilder.manifest` Reference；运行时
包只保留 `static.manifest`、`global.manifest`、`audio.manifest` 三个基础引用，避免与地图的
WorldBuilder Patch 链形成重复所有权。该步骤不删除 GameObject、武器或美术资产。

## 审计资产内容

1. 读 `Mod.xml.source`：所有 48 个模板（44 个 AttributeModifier + 4 个 GameObject）的声明、数值与语义一目了然——这就是注入游戏的全部内容，没有其他来源。
2. 对照 `asset-manifest.json`：每个模板的 InstanceID/类型/数值与 XML 一致；`officialIdBlacklist` 列出了永不进入包内的官方资产 ID（防止同 ID 覆盖官方模板）。
3. 校验二进制（可选）：重建后比对 `SHA256SUMS` 式哈希——见下节。

## 用自备 SDK 重建（可选）

前置：RA3 1.12 MOD SDK-X 工具链（`BinaryAssetBuilder.exe` / `HashFix.exe` / `ModAssetResolver.exe` 与 `builtmods\sagexml` 基线）、Steam 版 RA3 1.13 安装（首次运行需从中提取 WorldBuilder 美术流）。

```powershell
pwsh -File scripts/build-runtime-assets.ps1 -SdkRoot <你的 SDK-X 目录>
pwsh -File scripts/build-runtime-assets.ps1 -VerifyOnly   # 磁盘流 vs manifest 哈希
```

- Game 6（RA3 1.12）变体可完全自备工具重建；产物哈希应与 `asset-manifest.json` 一致。
- Game 7（Uprising）变体的最后一步（v6→v7 流头转换）依赖作者私有的 RA3-Uprising-Converter 工具，公开仓库不包含它；公开侧对 Game 7 的保证方式是**哈希锚定**（`asset-manifest.json` 的 `uprisingStreams` 块），而非可重建性。
- 没有 SDK 的机器（含 CI）：流缺失时构建仍通过（csproj `Condition="Exists"`），哈希合同测试显式 Skip，最终门禁在发布流程中本地强制执行。

## 哈希一致性的含义

`asset-manifest.json` 锁定八个流的 SHA-256。若你重建出的 Game 6 流哈希与 manifest 一致，说明你手上的二进制与官方发布内嵌的逐字节相同。任何不一致都意味着工具链版本或源发生了变化——此时应以 manifest 哈希为准并向议题反馈，而不是分发你重建出的流。

## 自建包不会被加载

RayaTrainer 只从自身程序集内嵌的资源释放并加载资产包（`RuntimeAssetPathProvider`），**没有从外部目录加载资产包的槽位或开关**。你无法通过替换磁盘文件让修改器加载自建的包内容；这份源码公开仅用于内容审计与可复现性验证。
