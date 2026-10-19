# RayaTrainer.App 子树指令

仓库级协作规则（项目状态入口、逆向真相源、idalib 工作流、发布约束）见根 `AGENTS.md`；本文件只承载 App 子树的主链路文件清单与合同约束，不维护协议版本、指纹等高漂移数值。

## 主链路文件

- 主界面协调：`ViewModels/MainViewModel.cs`
- 会话链（`TrainerSessionManager`/`TargetProcessHeartbeatMonitor`/诊断等）已迁至 `src/RayaTrainer.Host/Services/`，见 `src/RayaTrainer.Host/AGENTS.md`；App 仅消费，不要在 App 内重复实现会话状态
- 快捷键设置页：`ViewModels/HotkeySettingsViewModel.cs`（冲突检测、保存、恢复默认、`RequestReveal` 跳转定位）+ `Pages/HotkeySettingsPage.xaml`（code-behind 承载滚动高亮）+ `Controls/HotkeyCaptureControl.cs`（按键捕获控件）。页面索引一律经 `PageIds.ToIndex(PageIds.HotkeySettings)` 解析，不硬编码数字。功能徽章右键只有单项「修改快捷键…」：跳转本页并定位高亮对应行；注意 ContextMenu 的 MenuItem 绑定必须用 `RelativeSource={RelativeSource AncestorType=ContextMenu}` 取 `PlacementTarget`——MenuItem 自身没有该属性，`RelativeSource=Self` 会静默失效

## 合同约束

- Agent 控制面：`TrainerSessionManager`（Host 库）独占 controller/Patch 生命周期状态；逐功能可用性只通过 `GetFeatureCapability`，WPF 不自行组合布尔值与字符串原因
- 目标心跳：`TargetProcessHeartbeatMonitor` 每 2 秒探测 PID，连续 3 次失败后才由 `MainViewModel` 结束会话并标记离线；不要改回 UI 线程高频轮询
- 快捷键配置契约：`RayaTrainer.settings.json` 必须包含 `SchemaVersion: 2`，`Hotkeys` 字典只接受 `RawName` 键；旧 `Ra3Trainer.settings.json` 仅作为一次性迁移输入保留，当前格式不受支持时备份为 `RayaTrainer.settings.legacy.json` 后重置，不执行 DisplayName 或默认键迁移
- 快捷键运行时热重载：`MainViewModel.ReloadHotkeys(dict)` 是唯一入口——更新内存字典、刷新所有 `FeatureItemViewModel.Hotkey` 显示、若 patch 已安装则 Stop/Start `HotkeyOrchestrator` 重建 bindings、最后 `PersistSettings`。设置页保存与功能徽章右键改键都走这条路径，无需重启程序
- 动作热键声明表：新增主要按键（执行功能的键）只在 `MainViewModel.CreateActionHotkeyDefinitions` 加一条 `ActionHotkeyDefinition`（Core 的 `src/RayaTrainer.Core/Hotkeys/ActionHotkeyDefinition.cs`），默认值、设置页行、冲突检测、绑定、热重载与全局注册自动生效，不要在 MainViewModel 之外逐字段接线；全局动作按声明表顺序分配 RegisterHotKey id（`GlobalHotkeyIdBase` 起）。挂在既有 feature 上、无功能卡片的动作（给基地车/呼叫增援/复制单位）改在 `CreateFeatureCommandOverrides` 登记命令转发，其设置页行与默认键仍走 feature 管线
- 面向用户的默认口径按零基础电脑用户设计：主界面只突出一个动态“下一步”，技术选项默认折叠；错误文案必须说明用户接下来点击哪里，不能只返回 Agent、signature、Patch 等内部术语
- 功能定义与分组目录在 Core 侧维护，见 `src/RayaTrainer.Core/AGENTS.md`；`FeatureToggleViewModel.CreateGroups` 只渲染分组目录里列出的功能
- Web 已拆分为独立可选组件：App 不内置任何 Web 服务/ASP.NET Core/QRCoder/wwwroot（决策见 `docs/adr/0033-web-optional-component.md`，Web 宿主代码在 `src/RayaTrainer.Host/Web/`，可选组件入口在 `src/RayaTrainer.WebMini/`）；`TrainerWebHostLayoutTests` 反向守护禁止回流。设置字段 `EnableWebControl` 仅作历史兼容保留，App 不消费、保存时原样写回

## 验证入口

完成改动后先运行 `scripts/validate-active-doc-drift.ps1`；日常迭代用 `scripts/validate.ps1 -Quick`（必要时加 `-TestProject <csproj>` 跑受影响测试项目），发布/合并门禁用不带 `-Quick` 的全量档。
