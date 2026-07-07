# RAЯ Trainer

Command & Conquer: Red Alert 3（含 Uprising）单机关卡沙盒修改器。通过注入的 x86 Agent DLL 在运行时修改游戏内存，提供秘密协议解锁、增援单位注入、状态位开关等单机实验工具。

## ⚠️ 声明

1. **绝对不支持联机 / 多人模式。**  
   本工具直接修改游戏内存。**任何功能在联机或多人模式下使用都会导致数据不同步，立即闪退或断开连接。仅供单机使用。**

2. **不包含版权内容，不主张任何商标权利。**  
   本仓库仅包含原创源代码和预解析参考数据。不分发任何游戏素材、可执行文件、整合包或专有 SDK 材料。所有商标名称（Command & Conquer、Red Alert 3、EA）均为各自所有者的财产。本项目不主张也不暗示任何商标权利。

3. **不提供任何游戏副本的下载或分发。**  
   本工具不下载、捆绑或分发任何游戏安装程序、ISO、ROM 或二进制文件。使用本软件需要一份合法获取的 Command & Conquer: Red Alert 3 游戏副本。

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

1. 启动 Red Alert 3（支持 1.12、1.13、Uprising 1.0、Uprising 1.1 任一 profile）。
2. 启动训练器（`RayaTrainer.App.exe`）。
3. 训练器自动检测运行中的游戏 profile，注入 Agent DLL，启用功能面板。
4. 按需切换功能。所有修改仅在内存中进行，不修改任何游戏文件。

## 许可

Apache 2.0 — 详见 [LICENSE](LICENSE) 和 [NOTICE](NOTICE)。  
第三方组件列表见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

---

*本项目与 Electronic Arts Inc. 无关联，也未获得其认可。Command & Conquer 是 Electronic Arts 的商标。*
