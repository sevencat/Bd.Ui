# Bd.Ui

基于 Avalonia 的 bd（beads）桌面客户端：看板、Epic 进度与任务详情，所有数据来自本地 `bd` CLI。

## 功能一览（对照 scotty）

| 能力 | Bd.Ui | 说明 |
| --- | --- | --- |
| 五列看板（Backlog / Ready / In Progress / Blocked / Done） | 支持 | 列定义与 bd 状态一一映射 |
| 树状视图 | 支持 | 按 parent-child 递归展开，卡片可逐级展开/折叠，也可一键全部展开 |
| 拖拽移列 | 支持 | 乐观更新；失败自动回滚并提示原因；拖到 Blocked 只做本地标记，不写库 |
| 筛选 | 支持 | 搜索、类型、优先级、标签、包含已归档 |
| 新建 / 编辑任务 | 支持 | 标题、类型、优先级、负责人、描述；写操作串行化 |
| 任务详情 | 支持 | 评论、依赖、子任务、Epic 进度、创建者归属 |
| Epic 视图 | 支持 | 进度条、子任务完成数、归档过滤 |
| 评论 / 标签 | 支持 | 均通过 bd CLI 落库 |
| 归档 / 删除 | 支持 | 归档走标签约定，删除需确认 |
| human / agent 归属 | 支持 | 依据创建者与 allowlist 标注 |
| 只读模式 | 支持 | 写命令在 UI 层直接拒绝并说明原因 |
| 离线演示数据 | 支持 | 找不到 bd 时自动进入只读演示模式 |
| 自动刷新 | 支持 | 轮询间隔可配，写后立即刷新 |
| 明暗主题 / 快捷键 | 支持 | 见下方快捷键 |
| Focus 视图、多项目/多仓库、Web 端 | 未实现 | 与 scotty 的已知差异 |

## 环境要求

- Windows x64（当前发布目标为 `win-x64`）
- 开发：.NET SDK 10.0
- 运行：自包含发布版无需安装 .NET
- bd CLI 1.x（开发验证版本 1.3.4），可执行文件位于 `PATH` 或在设置中指定绝对路径
- 被浏览的目录必须是 bd 仓库（包含 `.beads/`）；没有仓库时先在目录内执行 `bd init`

## 快速开始

```powershell
# 1. 构建
dotnet build Bd.Ui.slnx

# 2. 运行（在 bd 仓库根目录执行最省事；也可在设置里填仓库路径）
dotnet run --project src/Bd.App

# 3. 测试
dotnet test Bd.Ui.slnx
```

首次启动若找不到 `bd`，应用不会报错退出，而是进入**只读演示模式**，可直接浏览看板、Epics 与详情。在「设置」中取消“演示模式”、填写仓库路径与 bd 路径后保存，即切换到真实仓库。

## 使用说明

- `Ctrl+N` 新建任务；`Ctrl+R` / `F5` 刷新；`Ctrl+F` 聚焦搜索。
- `Ctrl+1` 看板、`Ctrl+2` Epics、`Ctrl+3` 设置；`Esc` 关闭详情抽屉。
- 看板页顶部的「看板 / 树状」下拉切换视图；树状视图按父子关系缩进，点卡片左侧箭头展开或折叠，工具栏可一键「全部展开 / 全部折叠」。树状视图不支持拖拽移列。
- 拖拽卡片到 Done 会关闭任务；拖到 Blocked 只提示原因，不写库（bd 的阻塞由依赖决定）。
- 只读模式（设置项或演示数据）下，所有写入口直接拒绝并 toast 说明，不会产生 bd 子进程。

## 配置

配置文件：`%APPDATA%\Bd.Ui\config.json`（应用内「设置」页可修改大部分字段）。

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `RepoPath` | 空 | bd 仓库路径；空则使用当前工作目录 |
| `BdBinaryPath` | `bd` | bd 可执行文件，支持裸文件名（走 PATH）或绝对路径 |
| `HumanActor` | 空 | 视为“人”的操作者名称，用于归属标注与 allowlist |
| `HumanAllowlist` | 空 | 额外视为人的名称列表 |
| `PollSeconds` | 5 | 自动刷新间隔，1–300 秒 |
| `Theme` | `Dark` | `Dark` / `Light` |
| `ReadOnly` | `false` | 禁止所有写操作 |
| `DemoMode` | `false` | 强制使用内置演示数据，不启动 bd |
| `LogMaxFileSizeKb` | 2048 | 单个日志文件上限，超过后滚动 |
| `LogRetentionDays` | 7 | 归档日志保留天数 |
| `StorageBackend` | `cli` | `cli` 为正常模式；`demo` 等同演示模式 |

日志目录：`%APPDATA%\Bd.Ui\logs\bd-ui.log`，按大小滚动并清理过期归档。UI 线程、未观察任务与应用域异常都会带堆栈写入日志。

## 构建、测试与发布

```powershell
# 本地完整质量门禁（restore → build -warnaserror → test → publish 冒烟）
pwsh scripts/ci.ps1

# 只跑测试
dotnet test Bd.Ui.slnx

# 改版本号（写入 assembly 与「设置 → 关于」）
pwsh scripts/set-version.ps1 0.2.0

# 生成自包含单文件并打包 zip + SHA256
pwsh scripts/package.ps1
# 复用已有 artifacts/publish
pwsh scripts/package.ps1 -SkipBuild

# 重新生成程序图标（源文件是 Assets/beads.svg）
pwsh scripts/make-icon.ps1
```

发布产物：

- `artifacts/publish/Bd.Ui.exe`：自包含单文件，可在干净的 win-x64 机器直接运行
- `artifacts/package/Bd.Ui-<version>-win-x64.zip` 及同名 `.sha256`

CI 工作流见 `.github/workflows/ci.yml`，执行内容与 `scripts/ci.ps1` 完全一致。

## 程序图标

窗口与可执行文件使用 beads 官方 logo（`https://github.com/gastownhall/beads/blob/main/docs/images/logo.svg`：`#25C2A0` 圆角方块 + 白色粗体 `bd`）。

- `src/Bd.App/Assets/beads.svg`：官方源文件
- `src/Bd.App/Assets/beads.ico`：16/24/32/48/64/128/256 多尺寸图标，供 exe 与窗口使用
- `src/Bd.App/Assets/beads.png`：应用内 26px 品牌图标
- `scripts/make-icon.ps1`：从官方 logo 重绘上述 ico/png，改配色或尺寸后重跑即可

## 疑难排查

| 现象 | 处理 |
| --- | --- |
| 界面显示「演示数据 · 未连接 bd」 | 未找到 bd 或开启了演示模式；检查 PATH / `BdBinaryPath` / `DemoMode` |
| 提示不是 bd 仓库 | 目标目录缺少 `.beads/`，先执行 `bd init`，或在设置中改仓库路径 |
| `LockBusy` / 写操作繁忙 | bd 数据库被其他进程占用；等待重试，避免同时运行多个写操作 |
| 写操作被拒绝且提示只读 | 关闭只读模式，或当前处于演示数据 |
| 界面无任务但 bd 命令行有 | 确认仓库路径、包含已归档筛选与搜索条件 |
| 启动异常 | 查看 `%APPDATA%\Bd.Ui\logs\bd-ui.log` |

## 架构

分层、数据边界、并发模型与测试策略见 [docs/architecture.md](docs/architecture.md)。
