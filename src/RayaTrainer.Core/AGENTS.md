# RayaTrainer.Core 子树指令

仓库级协作规则（项目状态入口、逆向真相源、idalib 工作流、发布约束）见根 `AGENTS.md`；本文件只承载 Core 子树的主链路文件清单、合同约束与验证入口，不维护协议版本、指纹等高漂移数值。

## 主链路文件

- 功能入口：`Features/TrainerFeatureCatalog.cs`
- 功能共享分组：`Features/TrainerFeatureGroupCatalog.cs`
- 版本/profile 合同：`Versions/`
- DLL 内部功能状态（托管 ID）：`Agent/NativeFeatureStateId.cs`；Native 镜像位于 `src/RayaTrainer.Agent/AgentFeatureState.h`，两者必须同值
- Native Hook 稳定 ID：`Agent/NativeHookCatalog.cs`；hook 业务实现与分工见 `src/RayaTrainer.Agent/AGENTS.md`
- patch manifest：`Assets/trainer_report.json`
- Direct GameApi 真相源：`Agent/apis.json`；`src/**/Generated/` 不手改
- 诊断快照与能力模型：`Diagnostics/RuntimeDiagnosticModels.cs`

## Agent 身份合同（真相源指引）

协议版本、命令区间、capability mask、build fingerprint 与 Native catalog 条目数都是高漂移常量，本文件不维护具体数值，一律从真相源核对：

- 协议版本：`Agent/AgentProtocol.cs::Version` 与 `src/RayaTrainer.Agent/AgentProtocol.h::kAgentProtocolVersion`（两者必须同步）
- 命令编号与各版本演进史：这两个文件的 `AgentCommand` 枚举与头部注释
- capability mask：`AgentProtocol.h::kNativeRuntimeCapabilities`
- 托管指纹：`Agent/AgentBuildIdentity.cs::Fingerprint`；Native 镜像在 `AgentProtocol.h`
- catalog 条目数：`Agent/NativeAgentCatalog.cs::ExpectedEntryCount` 与 `AgentProtocol.h::NativeCatalogEntry::EntryCount`

稳定规则：只有协议与指纹都完全一致才可接管已注入 Agent，任何旧协议或旧指纹 Agent 一律拒绝接管且禁止二次注入。

## 修改 TrainerFeature 时的验证清单

新增或改动一个 `TrainerFeature` 时，下列各项必须保持一致，缺一项就会出现"功能装好了但 UI 看不到入口"或"点击后无响应"：

- `TrainerFeatureCatalog`：功能定义本身，含 `RawName`、`DisplayName`、`DispatchTarget`、`ValueHint`、`SupportedProfileIds`
- `TrainerFeatureGroupCatalog.Groups`：把 `DisplayName`（逐字符一致，含全角标点）加进某个分组的 `FeatureDisplayNames`。`FeatureToggleViewModel.CreateGroups` 只渲染分组目录里列出的功能，匹配不上的功能被静默丢弃
- Native Hook：若功能依赖 Hook，确认 `trainer_report.json`、`NativeHookCatalog.cs` 与 `src/RayaTrainer.Agent/AgentNativeHooks.cpp` 的 ID/返回标签一致
- `trainer_report.json`：若功能需要 hook，新增 `Hooks` 条目（`address`、`trampoline_target`、`return_label`、`original_assembly`、`supported_profiles`）
- 版本 profile：确认四个 profile 的 Hook RVA/expected bytes 与 Native catalog RVA/结构偏移已映射
- 签名扫描：若新增 hook 依赖签名扫描定位，在 `src/RayaTrainer.Agent/AgentSignatureScanner.cpp` 补签名条目
- Native 状态：开关/资源/pulse 使用 `NativeFeatureStateId.cs` 与 `AgentFeatureState.h` 的同值枚举；不得重新引入远端共享状态地址

## 验证入口

完成改动后先运行 `scripts/validate-active-doc-drift.ps1`；日常迭代用 `scripts/validate.ps1 -Quick`（文档漂移检查 + 增量编译 Core/App + x86 Agent，必要时加 `-TestProject <csproj>` 跑受影响测试项目），发布/合并门禁用不带 `-Quick` 的全量档。至少编译 Core + App 与 x86 Agent，涉及行为时再按风险运行 managed/native tests 和四 profile smoke。
