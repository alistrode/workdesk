---
name: workdesk
description: 运行、调试与维护 WorkDesk 一体工作台时使用；覆盖一键启动、模块与 GuIO 路径自动发现、端口配置、模块就绪诊断与共享材料目录。
---

# WorkDesk

## 何时使用

- 需要启动 WorkDesk 一体工作台（默认 <http://127.0.0.1:5190>）。
- 某个模块起不来、一直转圈，或 `/api/modules` 显示 `exeFound:false`。
- 换了机器 / 挪了目录，需要让 WorkDesk 找到三个子模块和 GuIO。
- 需要定位共享材料、运行日志或启动失败原因。

## 关键事实

| 项 | 值 |
|---|---|
| 监听地址 | `http://127.0.0.1:5190`（可用 `WORKDESK_PORT` 改） |
| 启动方式 | 双击 `Start.cmd`，或直接跑 `launcher\WorkDeskLauncher.exe` |
| 运行时 | Windows x64；两个 exe 均为自包含单文件，无需安装 .NET |
| 浏览器 | 优先 Edge，其次 Chrome，用 `--app=` 开独立窗口 |
| 共享材料 | `%LOCALAPPDATA%\WorkDesk\materials\*.json` |
| 日志 / 启动错误 | `%LOCALAPPDATA%\WorkDesk\logs\launcher-error.txt` |

## 模块路径自动发现（v1.1.0 起）

按优先级依次尝试，命中即止：

1. 环境变量 `WORKDESK_AGENTHUB_ROOT` / `WORKDESK_PROMPTLAB_ROOT` / `WORKDESK_RESEARCHDESK_ROOT` / `WORKDESK_GUIO_ROOT`
2. 配置文件 `workdesk.config.json` 的 `paths` 字段（相对路径以 `publish\` 为基准）
3. 同级标准名：`..\AgentHub`、`..\..\AgentHub`、`..\apps\AgentHub`、`AgentHub`
4. 模糊扫描：向上 1–4 层找名字含产品名、且内部确有 `publish\<产品>.exe` 的目录（支持再向下探一层，兼容 `researchdesk\researchdesk-1.1.0\publish\` 这种解压结构）
5. 旧固定路径兜底：`E:\xiangmujihua\apps\<产品>`

**判定标准是「目录里真的有 `publish\<产品>.exe`」**，同名空目录不算命中。

## 常用命令与等效文件索引

| 用途 | 命令 | 等效代码 / 脚本 |
|---|---|---|
| 启动（推荐） | 双击 `Start.cmd` | `Start.cmd` → `launcher\WorkDeskLauncher.exe` → `src/launcher/Program.cs` |
| 直接起核心服务 | `.\publish\WorkDesk.exe` | `src/workdesk/Program.cs` |
| 健康检查 | `Invoke-RestMethod http://127.0.0.1:5190/api/health` | `/api/health` 端点 |
| 查看三个模块状态 | `Invoke-RestMethod http://127.0.0.1:5190/api/modules` | `Modules.Status` |
| 手动拉起某模块 | `Invoke-RestMethod -Method Post http://127.0.0.1:5190/api/modules/agent/start` | `Modules.Ensure` |
| 列出共享材料 | `Invoke-RestMethod http://127.0.0.1:5190/api/materials` | `/api/materials` 端点 |
| 检查 GuIO | `Invoke-RestMethod -Method Post http://127.0.0.1:5190/api/guio/ping` | `/api/guio/{command}` 端点 |
| 换端口 | `$env:WORKDESK_PORT='5290'; .\publish\WorkDesk.exe` | `Modules` 构造函数 |
| 从源码构建核心服务 | `dotnet publish src\workdesk\WorkDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish` | `src/workdesk/WorkDesk.csproj` |
| 从源码构建启动器 | `dotnet publish src\launcher\WorkDeskLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o launcher` | `src/launcher/WorkDeskLauncher.csproj` |
| 查看启动错误 | `Get-Content "$env:LOCALAPPDATA\WorkDesk\logs\launcher-error.txt"` | 启动器 catch 分支 |
| 停止进程 | `Get-Process WorkDesk,WorkDeskLauncher -ErrorAction SilentlyContinue \| Stop-Process -Force` | — |

## 诊断顺序

1. 先看 `/api/modules`，逐项读 `root` / `exeFound` / `ready` / `hint`：
   - `root:null` → 完全没找到，按 `hint` 放包或设环境变量。
   - `exeFound:false` → 目录找到了但缺 `publish\<产品>.exe`。
   - `ready:false` + `exeFound:true` → 正常，点开页面会自动拉起。
2. `POST /api/modules/{key}/start` 返回的 `error` 就是 `Ensure` 抛出的原始原因。
3. 启动器用互斥量 `Local\WorkDesk.Start` 防重复拉起，卡住要等 45 秒才报错。
4. 跨域 403 是设计行为：只接受 `127.0.0.1`/`localhost`，非 GET 的 `Origin` 必须与 Host 一致。
5. 端口被占 → 设 `WORKDESK_PORT` 换端口，启动器会跟随。

## 目录说明

- `src/workdesk/` — 工作台核心服务源码
- `src/launcher/` — 原生启动器源码
- `README.md` — 用户安装与部署说明
- `workdesk.config.example.json` — 路径配置示例
- `MODELS.md` — 子模块所需模型的下载清单
- `verification.json` / `验收记录.md` — 历史校验与验收记录
- Release 附件 `workdesk-v*-win-x64.zip` — 自包含免安装分发包
