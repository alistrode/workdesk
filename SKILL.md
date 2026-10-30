---
name: workdesk
description: 运行、调试与维护 WorkDesk 一体工作台时使用；覆盖启动器拉起服务、模块端口映射、GuIO 设备通道、共享材料目录与绝对路径依赖排障。
---

# WorkDesk

## 何时使用

- 需要启动 WorkDesk 一体工作台（默认 http://127.0.0.1:5190）。
- 左侧「调研 / 提示词 / 智能体协作」某个模块起不来或一直转圈。
- 「设备」页面检查 GuIO 失败，或需要打开 GuIO 原生操作面板。
- 需要定位共享材料、运行日志或启动失败原因。

## 关键事实

| 项 | 值 |
|---|---|
| 监听地址 | `http://127.0.0.1:5190`（硬编码，仅回环） |
| 启动方式 | `launcher\WorkDeskLauncher.exe`（原生启动器，不需要改 PowerShell 执行策略） |
| 运行时 | Windows x64；两个 exe 均为自包含单文件，无需安装 .NET |
| 浏览器 | 优先 Edge，其次 Chrome，用 `--app=` 开独立窗口；都没有则走默认浏览器 |
| 共享材料 | `%LOCALAPPDATA%\WorkDesk\materials\*.json` |
| 日志 / 启动错误 | `%LOCALAPPDATA%\WorkDesk\logs\launcher-error.txt` |

## ⚠️ 绝对路径依赖（重要）

`src/workdesk/Program.cs` 里模块根目录和 GuIO 路径都是**硬编码的绝对路径**，不随解压位置变化：

| 用途 | 硬编码路径 |
|---|---|
| ResearchDesk 模块 | `E:\xiangmujihua\apps\ResearchDesk` |
| PromptLab 模块 | `E:\xiangmujihua\apps\PromptLab` |
| AgentHub 模块 | `E:\xiangmujihua\apps\AgentHub` |
| GuIO 主程序 | `C:\Users\Acer\GuIO\guio-app.exe` |
| GuIO CLI | `C:\Users\Acer\GuIO\cli\guio.exe` |

因此 WorkDesk **不是单文件夹自洽的**：核心服务（5190 端口、共享材料、GUI 外壳）可以独立运行，
但三个子模块与 GuIO 必须在上述路径存在，否则对应页面会报「启动失败 / 安装文件不存在」。
要迁移到别的机器，需先改 `src/workdesk/Program.cs` 里 `Modules.Items` 和两处 GuIO 路径，再重新发布。

## 常用命令与等效文件索引

| 用途 | 命令 | 等效代码 / 脚本 |
|---|---|---|
| 启动（推荐） | `.\launcher\WorkDeskLauncher.exe` | `src/launcher/Program.cs` |
| 直接起核心服务 | `.\publish\WorkDesk.exe` | `src/workdesk/Program.cs` |
| 健康检查 | `Invoke-RestMethod http://127.0.0.1:5190/api/health` | `src/workdesk/Program.cs` 的 `/api/health` 端点 |
| 查看三个模块就绪状态 | `Invoke-RestMethod http://127.0.0.1:5190/api/modules` | `src/workdesk/Program.cs` → `Modules.Status` |
| 手动拉起某模块 | `Invoke-RestMethod -Method Post http://127.0.0.1:5190/api/modules/research/start` | `Modules.Ensure` |
| 列出共享材料 | `Invoke-RestMethod http://127.0.0.1:5190/api/materials` | `src/workdesk/Program.cs` 的 `/api/materials` 端点 |
| 检查 GuIO | `Invoke-RestMethod -Method Post http://127.0.0.1:5190/api/guio/ping` | `src/workdesk/Program.cs` 的 `/api/guio/{command}` 端点 |
| 从源码构建核心服务 | `dotnet publish src\workdesk\WorkDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish` | `src/workdesk/WorkDesk.csproj` |
| 从源码构建启动器 | `dotnet publish src\launcher\WorkDeskLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o launcher` | `src/launcher/WorkDeskLauncher.csproj` |
| 查看启动错误 | `Get-Content "$env:LOCALAPPDATA\WorkDesk\logs\launcher-error.txt"` | `src/launcher/Program.cs` 的 catch 分支 |
| 停止进程 | `Get-Process WorkDesk,WorkDeskLauncher -ErrorAction SilentlyContinue \| Stop-Process -Force` | — |

## 排障顺序

1. 启动器用互斥量 `Local\WorkDesk.Start` 防重复拉起，卡住等 45 秒后才报错。
2. 模块页面报错 → 先 `Invoke-RestMethod http://127.0.0.1:5190/api/modules` 看 `ready` 字段。
3. `ready=false` 且提示「安装文件不存在」→ 对应绝对路径下缺 `publish\<Product>.exe`。
4. 跨域 403 是设计行为：只接受 `127.0.0.1` / `localhost`，且非 GET 请求的 `Origin` 必须与 Host 一致。
5. 启动器报「另一个启动器仍在启动工作台」→ 等前一个退出，或结束残留 `WorkDeskLauncher` 进程。

## 目录说明

- `src/workdesk/` — 工作台核心服务源码
- `src/launcher/` — 原生启动器源码
- `README.md` — 产品说明
- `verification.json` — 四个主要可执行文件的 SHA256 校验记录
- `验收记录.md` — 本次整合的验收结论
- Release 附件 `workdesk-v*-win-x64.zip` — 自包含免安装分发包
