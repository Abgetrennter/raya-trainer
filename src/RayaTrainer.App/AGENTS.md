# RayaTrainer.App 子树指令

仓库级协作规则（项目状态入口、逆向真相源、idalib 工作流、发布约束）见根 `AGENTS.md`；本文件只承载 App 子树的主链路文件清单与合同约束，不维护协议版本、指纹等高漂移数值。

## 主链路文件

- 主界面协调：`ViewModels/MainViewModel.cs`
- 会话诊断采集：`Services/TrainerSessionManager.cs`；不要把结构化诊断重新拼回 `StatusMessage`
- 目标心跳：`Services/TargetProcessHeartbeatMonitor.cs`
- 快捷键设置页：`ViewModels/HotkeySettingsViewModel.cs`（冲突检测、保存、恢复默认）+ `Pages/HotkeySettingsPage.xaml` + `Controls/HotkeyCaptureControl.cs`（按键捕获控件）。侧边栏第 7 项（`SelectedPageIndex=6`）

## 合同约束

- Agent 控制面：`TrainerSessionManager` 独占 controller/Patch 生命周期状态；逐功能可用性只通过 `GetFeatureCapability`，WPF/Web 不自行组合布尔值与字符串原因
- 目标心跳：`TargetProcessHeartbeatMonitor` 每 2 秒探测 PID，连续 3 次失败后才由 `MainViewModel` 结束会话并标记离线；不要改回 UI 线程高频轮询
- 快捷键配置契约：`RayaTrainer.settings.json` 必须包含 `SchemaVersion: 2`，`Hotkeys` 字典只接受 `RawName` 键；旧 `Ra3Trainer.settings.json` 仅作为一次性迁移输入保留，当前格式不受支持时备份为 `RayaTrainer.settings.legacy.json` 后重置，不执行 DisplayName 或默认键迁移
- 快捷键运行时热重载：`MainViewModel.ReloadHotkeys(dict)` 是唯一入口——更新内存字典、刷新所有 `FeatureItemViewModel.Hotkey` 显示、若 patch 已安装则 Stop/Start `HotkeyOrchestrator` 重建 bindings、最后 `PersistSettings`。设置页保存与功能徽章右键改键都走这条路径，无需重启程序
- 面向用户的默认口径按零基础电脑用户设计：主界面只突出一个动态“下一步”，技术选项默认折叠；错误文案必须说明用户接下来点击哪里，不能只返回 Agent、signature、Patch 等内部术语
- 功能定义与分组目录在 Core 侧维护，见 `src/RayaTrainer.Core/AGENTS.md`；`FeatureToggleViewModel.CreateGroups` 只渲染分组目录里列出的功能

## 验证入口

完成改动后先运行 `scripts/validate-active-doc-drift.ps1`；日常迭代用 `scripts/validate.ps1 -Quick`（必要时加 `-TestProject <csproj>` 跑受影响测试项目），发布/合并门禁用不带 `-Quick` 的全量档。
