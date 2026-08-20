# RAЯ Trainer

Command & Conquer: Red Alert 3（含 Uprising）单机关卡沙盒修改器。通过注入的 x86 Agent DLL 在运行时修改游戏内存，提供秘密协议解锁、增援单位注入、状态位开关等单机实验工具。

## ⚠️ 声明

1. **绝对不支持联机 / 多人模式。**  
   本工具直接修改游戏内存。**任何功能在联机或多人模式下使用都会导致数据不同步，立即闪退或断开连接。仅供单机使用。**

2. **不包含版权内容，不主张任何商标权利。**  
   本仓库仅包含原创源代码和预解析参考数据。不分发任何游戏素材、可执行文件、整合包或专有 SDK 材料。所有商标名称（Command & Conquer、Red Alert 3、EA）均为各自所有者的财产。本项目不主张也不暗示任何商标权利。

3. **不提供任何游戏副本的下载或分发。**  
   本工具不下载、捆绑或分发任何游戏安装程序、ISO、ROM 或二进制文件。使用本软件需要一份合法获取的 Command & Conquer: Red Alert 3 游戏副本。

4. **公开功能范围限制。**
   选中单位页面和热键功能已通过实机验证。攻击范围扩展与攻击速度调整不列入公开 UI 或受支持范围。逻辑时间冻结与慢动作仅在 RA3 1.12 通过验证。本工具不提供 DRM 绕过、反作弊绕过或任何形式访问限制规避功能。

5. **默认完全离线，不主动联网。**
   本工具启动时不会监听任何网络端口，不建立出站网络连接，不上传遥测；主程序也不再依赖 ASP.NET Core 运行库（仅需 .NET 8 x86 Desktop Runtime）。网页/手机遥控是一项**独立可选组件**：不再内置于主程序，需要时单独运行同一 Release 附带的 `RayaTrainer.WebMini.exe`（可选附件包，framework-dependent 版需另装 ASP.NET Core Runtime x86，Windows Desktop Runtime 与主程序共用，self-contained 版无需），它会自动附加游戏并创建本地 HTTP 服务（默认端口 8787，可在窗口修改），窗口内显示配对二维码与连接地址，支持修改端口与绑定网卡；供同一局域网的手机浏览器访问，首次连接需在其窗口内确认设备配对。该组件仅在局域网被动监听，不向 EA 或任何外部服务上报数据。详见透明度报告与 ADR 0033。

## 仓库结构

本仓库是私有主仓，同时挂载一个逆向分析知识库子模块，并通过投影生成公开仓的发布产物。三者关系如下：

- **私有主仓（本仓）**：完整真相源，包含 App、Core、Native Agent、生成器、全量测试和发布工具。
- **`RA3_Analysis/`（子模块）**：挂载的私有逆向分析知识库（`RA3-Engine-Atlas`，固定 commit gitlink），独立维护修改器使用的运行时地址、Hook 语义和引擎机制证据。它可独立 clone、审计和测试，但普通构建与发布不依赖它。
- **公开投影**：经 `scripts/migrate-to-public.ps1` 从本仓投影到公开仓，只包含 Managed 外壳、公共测试和法律文件。Native Agent 源码与知识库子模块不进入投影，编译后的 Agent 仅以二进制形式随 Release 发布。

下方的「仓库内容」描述的是私有主仓目录。

## 仓库内容

| 目录 | 说明 |
|------|------|
| `src/RayaTrainer.Core/` | 托管库：训练器功能、协议定义、资产包加载 |
| `src/RayaTrainer.App/` | WPF 桌面 UI |
| `src/RayaTrainer.Agent/` | x86 C++ Agent DLL（注入到游戏进程） |
| `tests/` | xUnit 托管测试和原生测试 |
| `tools/` | 构建时代码生成器和验证工具 |
| `scripts/` | 构建、发布、验证和公开迁移脚本 |
| `Assets/Catalogs/` | 版本化、哈希验证的资产包（Corona 模组数据、参考注释） |

## 构建

环境要求：
- Windows 10+ (x64)
- .NET 8 SDK
- Visual Studio 2022 含 C++ 桌面工作负载（用于 x86 Agent DLL）

```powershell
# 构建托管解决方案 + x86 Agent
.\scripts\publish.ps1

# 或分步执行：
dotnet build RayaTrainer.sln -c Release
MSBuild.exe src/RayaTrainer.Agent/RayaTrainer.Agent.vcxproj /p:Configuration=Release /p:Platform=Win32
```

构建产物位于 `artifacts/` 目录。

## 使用

1. 启动 Red Alert 3（支持 1.12、1.13、Uprising 1.0、Uprising 1.1 任一 profile），**仅在单机模式下游玩。**
2. 启动训练器（`RayaTrainer.App.exe`）。
3. 训练器自动检测运行中的游戏 profile，注入 Agent DLL，启用功能面板。
4. 按需切换功能。所有修改仅在内存中进行，不修改任何游戏文件。

### Operation Explorer（实验性）

- 普通模式只显示已经完成实机验收的 `Productized` Operation。
- 高级模式允许直接查看和调用 `Executable` / `Verified` Operation，并按当前游戏动态生成参数表单；`Executable` 只表示参数、ABI 与 Route 已解码，不保证效果、恢复或游戏稳定性。
- 结果页会分别显示派发状态、引擎返回值与 Effect Evidence；没有读回证据时保持 `Unknown`，不会显示成“已验证成功”。
- Custom RecipePlan 只接受有界的类型化 JSON（Literal、PlanInput、StepOutput、显式 compensation），不接受 Lua 文本、循环、等待或 Route override。

## 版本兼容与验证状态

| 游戏版本 | 当前状态 |
|---|---|
| RA3 1.12 | 主链路、主要功能、Steam English 60fps 布局和游戏内 ImGui Overlay 已完成对应实机验收 |
| RA3 1.13 | 签名、Hook 与 Agent 安装合同已静态验证；当前行为 smoke 待补 |
| Uprising 1.0 | 签名、Hook 与 Agent 安装合同已静态验证；行为 smoke 待补 |
| Uprising 1.1 | 快速建造已实机验证；其余跨版本功能仍需继续 smoke |

v0.0.7 修正了 Uprising 快速建造完成时刻字段的版本差异，并轮换 Agent 构建身份。若从旧版本升级，请先彻底退出游戏再启动新版本，避免游戏进程继续保留旧版已注入 DLL。静态支持表示地址、签名、原字节和安装合同已验证，不代表所有功能都完成了对应版本的实机验收。

## 许可

Apache 2.0 — 详见 [LICENSE](LICENSE) 和 [NOTICE](NOTICE)。  
第三方组件列表见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

---

*本项目与 Electronic Arts Inc. 无关联，也未获得其认可。Command & Conquer 是 Electronic Arts 的商标。*
