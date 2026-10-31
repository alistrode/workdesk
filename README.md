# WorkDesk 一体工作台

双击 `Start.cmd`（或 `launcher\WorkDeskLauncher.exe`）即可启动，浏览器会以独立窗口打开 <http://127.0.0.1:5190/>。

- 两个 exe 都是 .NET 10 单文件自包含，**不需要安装 .NET**
- 不需要管理员权限，不写注册表
- 不需要修改 PowerShell 执行策略（启动器是原生 exe）

## 一、推荐部署方式：四个包解压到同一父目录

```text
我的工具\
  workdesk-1.1.0\
  agenthub-1.1.0\
  promptlab-1.1.0\
  researchdesk-1.1.0\
  GuIO\                （可选，设备页需要）
```

文件夹名不必叫 `AgentHub`，只要名字里含对应产品名即可（`agenthub-1.1.0` 能匹配 `AgentHub`）。
WorkDesk 启动时会自己找同级目录里的模块，**不需要改任何配置**。

## 二、模块是怎么被找到的

按以下优先级依次尝试，命中即止：

1. 环境变量：`WORKDESK_AGENTHUB_ROOT` / `WORKDESK_PROMPTLAB_ROOT` / `WORKDESK_RESEARCHDESK_ROOT` / `WORKDESK_GUIO_ROOT`
2. 配置文件 `workdesk.config.json` 里的 `paths` 字段（复制 `workdesk.config.example.json` 改）
3. 同级目录按标准名匹配：`..\AgentHub`、`..\..\AgentHub`、`..\apps\AgentHub`、`AgentHub`
4. 同级目录**模糊扫描**：在上级 1–3 层里找名字含产品名、且内部确实存在 `publish\<产品>.exe` 的目录
5. 旧机器遗留固定路径兜底：`E:\xiangmujihua\apps\<产品>`

**判定标准是「目录里真的有 `publish\<产品>.exe`」**，光有同名文件夹不算命中。

## 三、故障怎么判断

打开 <http://127.0.0.1:5190/api/modules>，会逐项返回：

```json
{"key":"agent","name":"AgentHub","port":5178,
 "root":"...\\agenthub-1.1.0","exeFound":true,"ready":true,"hint":null}
```

| 字段 | 含义 |
|---|---|
| `root` | 实际使用的是哪个目录；`null` 表示没找到 |
| `exeFound` | 该目录下是否真有 `publish\<产品>.exe` |
| `ready` | 该模块服务当前是否已在跑（健康检查通过） |
| `hint` | 没找到或文件缺失时的具体原因 |

`ready:false` 且 `exeFound:true` 属正常——点开对应页面时 WorkDesk 会自动拉起它。
`exeFound:false` 才是部署问题，按 `hint` 处理。

## 四、可配置项

| 环境变量 | 默认 | 说明 |
|---|---|---|
| `WORKDESK_PORT` | `5190` | 工作台自身端口 |
| `WORKDESK_AGENTHUB_ROOT` | 自动发现 | AgentHub 安装目录 |
| `WORKDESK_PROMPTLAB_ROOT` | 自动发现 | PromptLab 安装目录 |
| `WORKDESK_RESEARCHDESK_ROOT` | 自动发现 | ResearchDesk 安装目录 |
| `WORKDESK_GUIO_ROOT` | 自动发现 | GuIO 目录（含 `guio-app.exe`） |

也可以写 `workdesk.config.json`（放在 `publish\WorkDesk.exe` 同级）：

```json
{
  "paths": {
    "agent": "..\\AgentHub",
    "prompt": "..\\PromptLab",
    "research": "..\\ResearchDesk",
    "guio": "..\\GuIO"
  }
}
```

相对路径以 `publish\` 目录为基准。

## 五、使用

1. 左侧切换调研、提示词调优、智能体协作；模块按需启动，切换时保留页面。
2. 打开模块中的结果，点击顶部「收集当前结果」存入共享材料。
3. 编辑共享材料，发送到调研问题、提示词草稿或新协作目标，检查后自行开始处理。
4. 设备页面可检查 GuIO、列出设备并打开原生操作面板。

提示词只传递已采纳候选；没有采纳时传递原版。调研问题最多 1000 字。共享材料可导入、导出 JSON。

## 六、数据与日志

| 内容 | 路径 |
|---|---|
| 共享材料 | `%LOCALAPPDATA%\WorkDesk\materials` |
| 启动器错误 | `%LOCALAPPDATA%\WorkDesk\logs\launcher-error.txt` |

## 七、边界

- 本工作台只监听回环地址，非 `127.0.0.1`/`localhost` 一律 403；非 GET 请求的 `Origin` 必须与 Host 一致。
- 关闭工作台窗口不会中断模块中正在运行的任务；再次启动会复用仍在运行的服务。
- GuIO 是可选的。找不到 GuIO 时，设备页会返回明确提示，其余功能不受影响。
- 三个子模块各自需要本机 Ollama 与至少一个模型，详见 `MODELS.md`。
