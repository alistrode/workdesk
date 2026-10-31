# 依赖清单

## 运行分发包 `workdesk-v*-win-x64.zip`

核心服务与启动器都是自包含单文件，不需要安装 .NET、不需要管理员权限、不写注册表。

| 依赖 | 是否必须 | 说明 |
|---|---|---|
| Windows x64 | 必须 | 目标框架 .NET 10.0，运行时已内嵌进两个 exe |
| .NET 10 运行时 | 不需要 | 单文件自包含 |
| Edge 或 Chrome | 推荐 | 启动器用 `--app=` 开独立窗口；都没有则回退默认浏览器 |
| ResearchDesk / PromptLab / AgentHub | **必需（模块功能）** | 放同级目录即自动发现，详见下节 |
| GuIO | 可选（设备页） | 放同级目录即自动发现 |
| 本机 Ollama | 间接必需 | 由三个子模块各自使用，见 `MODELS.md` |

## 模块路径：自动发现，不再写死

v1.1.0 起 `src/workdesk/Program.cs` 不再硬编码绝对路径，改为按优先级解析：

1. 环境变量：`WORKDESK_AGENTHUB_ROOT`、`WORKDESK_PROMPTLAB_ROOT`、`WORKDESK_RESEARCHDESK_ROOT`、`WORKDESK_GUIO_ROOT`
2. 配置文件 `workdesk.config.json`（放在 `publish\WorkDesk.exe` 同级）的 `paths` 字段
3. 同级标准名：`..\AgentHub`、`..\..\AgentHub`、`..\apps\AgentHub`、`AgentHub`
4. 模糊扫描：向上 1–4 层按名字匹配，并支持向下一层探（兼容 `researchdesk\researchdesk-1.1.0\publish\` 结构）
5. 旧固定路径兜底：`E:\xiangmujihua\apps\<产品>`

判定标准是**目录里确实存在 `publish\<产品>.exe`**。

推荐部署结构（文件夹名含产品名即可，不必完全一致）：

```text
我的工具\
  workdesk-1.1.0\
  agenthub-1.1.0\
  promptlab-1.1.0\
  researchdesk-1.1.0\
  GuIO\              （可选）
```

## 从源码构建

| 依赖 | 版本 |
|---|---|
| .NET SDK | 10.0（本项目在 10.0.401 上验证） |
| 目标框架 | `net10.0`（`WorkDeskLauncher` 为 `WinExe`） |
| 程序集依赖 | 仅 `Microsoft.NET.Sdk.Web` / `Microsoft.NET.Sdk`，无第三方 NuGet 包 |
| 自包含发布 | 需要 `Microsoft.NETCore.App.Runtime.win-x64` 运行时包（首次 `dotnet publish` 会联网还原） |

## 端口

| 环境变量 | 默认 | 说明 |
|---|---|---|
| `WORKDESK_PORT` | `5190` | 工作台自身端口，启动器与健康检查都会跟随 |

子模块端口固定在各自子模块里：ResearchDesk 5182、PromptLab 5181、AgentHub 5178。

## 数据目录

| 路径 | 内容 |
|---|---|
| `%LOCALAPPDATA%\WorkDesk\materials` | 共享材料 JSON |
| `%LOCALAPPDATA%\WorkDesk\logs` | 启动器错误日志 |
