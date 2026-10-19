# RayaTrainer.Host 子树指令

仓库级协作规则（项目状态入口、逆向真相源、idalib 工作流、发布约束）见根 `AGENTS.md`；本文件只承载 Host 库的主链路文件清单与合同约束。

Host 是无 UI 会话宿主库：承载 Web 遥控所需的会话链、产品管线、预设投影、诊断与 ASP.NET Core Web 宿主。主程序（`RayaTrainer.App`，经 Overlay/WPF 消费）与独立可选组件（`RayaTrainer.WebMini`，原生 WinForms 单窗口）都引用本库。

## 硬性边界

- **对 WPF 零依赖**：本库任何源码不得出现 `System.Windows*`；`Public.Tests` 的 `PublicHostAndWebMiniStayWpfFreeAndProjectable` 会扫描全树拦截。UI 线程调度需求通过抽象接口回推给宿主（如 `IFeatureToggleCoordinator` 由 App 实现并自行经 Dispatcher 调度）。
- **ASP.NET Core 框架引用 `PrivateAssets="all"`**：Host 声明 `Microsoft.AspNetCore.App` 但不传递给引用方——主程序 runtimeconfig 必须保持无 ASP.NET Core。需要 Web 宿主的消费方（WebMini、测试）必须自行声明框架引用。
- **`Private/` 不进公开投影**：`Private/Services/` 的 Script Operation 分部扩展在公开投影下被 `Compile Remove` 排除，与 App/Core 的 Private 边界同语义；新增私有扩展必须放在 `Private/` 下。

## 主链路文件

- 会话链：`Services/TrainerSessionManager.cs`（附加/Patch/诊断聚合，独占 controller/Patch 生命周期）、`Services/InjectedAgentBackend.cs`（Agent 管道后端）、`Services/GameProcessWatcher.cs`（游戏进程发现）、`Services/TargetProcessHeartbeatMonitor.cs`（目标心跳）
- Web 宿主：`Web/TrainerWebHost.cs`（Kestrel 宿主组装，审批服务经可选参数注入，未注入默认 fail-closed）、`Web/TrainerApiEndpoints.cs`、`Web/TrainerApiHandler.cs`、`Web/wwwroot/`（PWA 前端资产，经 `AppContext.BaseDirectory/Web/wwwroot` 定位）
- 预设投影：`Services/ReinforcementPresetProjectionCoordinator.cs`、`Services/SecretProtocolPresetProjectionCoordinator.cs`

## 合同约束

- 会话诊断采集：不要把结构化诊断重新拼回宿主侧 `StatusMessage`；诊断快照经 `ITrainerDiagnosticsSource` 原样透出。
- Web 开关请求路径：注入了 `IFeatureToggleCoordinator` 走桌面 desired-state 协调；未注入（WebMini）走 `IGameApiCommandQueue` 直控 fallback。两条路径都不得绕过 `RequireController`/capability 门禁。
- 设备配对审批：`IDeviceApprovalService` 由宿主注入（App 曾提供 WPF 弹窗实现，已随剥离删除；WebMini 用窗口弹窗实现）；`TrainerWebHost.Create` 未收到实现时注册 `InMemoryDeviceApprovalService(false)`（默认拒绝），不得改为默认放行。
- 崩溃日志：`RayaTrainerCrashLog` 是本库公开的兜底入口，所有写入必须吞异常。

## 验证入口

完成改动后先运行 `scripts/validate-active-doc-drift.ps1`；日常迭代用 `scripts/validate.ps1 -Quick`（必要时加 `-TestProject <csproj>` 跑受影响测试项目），发布/合并门禁用不带 `-Quick` 的全量档。
