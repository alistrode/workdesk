# 依赖清单

## 运行分发包 `workdesk-v*-win-x64.zip`

核心服务与启动器都是自包含单文件，不需要安装 .NET、不需要管理员权限、不写注册表。

| 依赖 | 是否必须 | 说明 |
|---|---|---|
| Windows x64 | 必须 | 目标框架 .NET 10.0，运行时已内嵌进两个 exe |
| .NET 10 运行时 | 不需要 | 单文件自包含 |
| Edge 或 Chrome | 推荐 | 启动器用 `--app=` 开独立窗口；都没有则回退默认浏览器 |
| ResearchDesk / PromptLab / AgentHub | **必需（模块功能）** | 必须位于硬编码绝对路径，见下 |
| GuIO | **必需（设备页）** | 必须位于 `C:\Users\Acer\GuIO` |
| 互联网 | 视模块而定 | 调研模块需联网 |

## 硬编码绝对路径（不可移动）

| 用途 | 路径 |
|---|---|
| ResearchDesk | `E:\xiangmujihua\apps\ResearchDesk`（含 `publish\ResearchDesk.exe`） |
| PromptLab | `E:\xiangmujihua\apps\PromptLab`（含 `publish\PromptLab.exe`） |
| AgentHub | `E:\xiangmujihua\apps\AgentHub`（含 `publish\AgentHub.exe`） |
| GuIO 主程序 | `C:\Users\Acer\GuIO\guio-app.exe` |
| GuIO CLI | `C:\Users\Acer\GuIO\cli\guio.exe` |

这些路径写死在 `src/workdesk/Program.cs` 的 `Modules.Items` 与两处 `ProcessStartInfo` 里。
**因此本包不是单文件夹自洽的**：5190 核心服务与共享材料可独立跑，但模块和 GuIO 依赖上述位置存在。

## 从源码构建

| 依赖 | 版本 |
|---|---|
| .NET SDK | 10.0 |
| 目标框架 | `net10.0`（`WorkDeskLauncher` 为 `WinExe`） |
| 程序集依赖 | 仅 `Microsoft.NET.Sdk.Web` / `Microsoft.NET.Sdk`，无第三方 NuGet 包 |

## 端口

| 端口 | 用途 |
|---|---|
| 5190 | WorkDesk 核心服务（仅绑定 127.0.0.1） |
| 5182 | 子模块 ResearchDesk（硬编码） |
| 5181 | 子模块 PromptLab（硬编码） |
| 5178 | 子模块 AgentHub（硬编码） |

## 数据目录

| 路径 | 内容 |
|---|---|
| `%LOCALAPPDATA%\WorkDesk\materials` | 共享材料 JSON |
| `%LOCALAPPDATA%\WorkDesk\logs` | 启动器错误日志 |
