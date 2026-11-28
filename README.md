# Bd.Ui

基于 Avalonia 的 bd（beads）桌面客户端：看板、Epic 进度与任务详情，所有数据直接读写 Dolt 服务器（MySQL 协议），不调用 `bd` 命令行。

## 功能一览（对照 scotty）

| 能力 | Bd.Ui | 说明 |
| --- | --- | --- |
| 五列看板（Backlog / Ready / In Progress / Blocked / Done） | 支持 | 列定义与 bd 状态一一映射 |
| 树状视图 | 支持 | 按 parent-child 递归展开，卡片可逐级展开/折叠，也可一键全部展开 |
| 拖拽移列 | 支持 | 乐观更新；失败自动回滚并提示原因；拖到 Blocked 只做本地标记，不写库 |
| 新建任务 | 支持 | 标题、类型、优先级、负责人、描述；写操作串行化 |
| 任务详情 | 支持 | 只读展示状态/优先级/指派人，含评论、依赖、子任务、Epic 进度、创建者归属 |
| Epic 视图 | 支持 | 进度条、子任务完成数、归档过滤 |
| 评论 / 标签 | 支持 | 直接写 Dolt 的 comments / labels 表 |
| 归档 / 删除 | 支持 | 归档走标签约定，删除需确认 |
| human / agent 归属 | 支持 | 依据创建者与 allowlist 标注 |
| 只读模式 | 支持 | 写操作在 UI 层直接拒绝并说明原因 |
| 离线演示数据 | 支持 | 显式开启演示模式，内置只读示例数据 |
| 自动刷新 | 不提供 | 只按需刷新：点「刷新」/ 写操作完成 / 保存设置 |
| 明暗主题 / 快捷键 | 支持 | 见下方快捷键 |
| Focus 视图、多项目/多仓库、Web 端 | 未实现 | 与 scotty 的已知差异 |

## 环境要求

- Windows x64（当前发布目标为 `win-x64`）
- 开发：.NET SDK 10.0
- 运行：自包含发布版无需安装 .NET
- 可访问的 Dolt 服务器（`dolt sql-server`），库里是 beads 的表结构
- 连接密码写在 config.json（也可留空，改用环境变量 `BEADS_DOLT_PASSWORD`）

## 快速开始

```powershell
# 1. 构建
dotnet build Bd.Ui.slnx

# 2. 运行（默认连团队共享 Dolt，库名 king_bdui，可在设置页改）
# 默认口令已写入配置；如需改用环境变量：$env:BEADS_DOLT_PASSWORD = "<你的密码>"
dotnet run --project src/Bd.App

# 3. 测试
dotnet test Bd.Ui.slnx
```

首次启动会直接连接 Dolt：连不上时界面顶部提示错误、看板为空，可在「设置」里改主机/端口/库名后点「测试连接」。想先离线看界面，就勾选「演示模式」——内置一份只读示例数据，不访问任何服务器。

## 使用说明

- `Ctrl+N` 新建任务；`Ctrl+R` / `F5` 刷新。
- `Ctrl+1` 看板、`Ctrl+2` Epics、`Ctrl+3` 设置；`Esc` 关闭详情抽屉。
- 看板页顶部的「看板 / 树状」下拉切换视图；树状视图按父子关系缩进，点卡片左侧箭头展开或折叠，工具栏可一键「全部展开 / 全部折叠」。树状视图不支持拖拽移列。
- 拖拽卡片到 Done 会关闭任务；拖到 Blocked 只提示原因，不写库（beads 的阻塞由依赖决定）。
- 只读模式（设置项或演示数据）下，所有写入口直接拒绝并 toast 说明，不会向服务器发出写语句。

## 配置

配置文件：`%APPDATA%\Bd.Ui\config.json`（应用内「设置」页可修改大部分字段）。

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `DoltHost` | 团队共享服务器 | Dolt 服务器主机，默认值见 `src/Bd.Core/Config/DoltDefaults.cs` |
| `DoltPort` | `7050` | Dolt 服务器端口（MySQL 协议） |
| `DoltUser` | `nwms` | 连接用户 |
| `DoltPassword` | 默认口令 | 连接密码，明文存储；留空则读取环境变量 `BEADS_DOLT_PASSWORD` |
| `DoltDatabase` | `king_bdui` | 目标库，一个库即一个项目 |
| `DoltUseTls` | `false` | 连接启用 TLS |
| `DoltAggregateAll` | `false` | 合并服务器上所有 beads 库为「全部项目」（只读） |
| `HumanActor` | 空 | 视为“人”的操作者名称，用于归属标注与 allowlist |
| `HumanAllowlist` | 空 | 额外视为人的名称列表 |
| `Theme` | `Dark` | `Dark` / `Light` |
| `ReadOnly` | `false` | 禁止所有写操作 |
| `DemoMode` | `false` | 使用内置演示数据，不连接 Dolt |
| `LogMaxFileSizeKb` | 2048 | 单个日志文件上限，超过后滚动 |
| `LogRetentionDays` | 7 | 归档日志保留天数 |

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
| 界面显示「演示数据 · 未连接 Dolt」 | 开启了演示模式；在「设置」取消勾选并保存 |
| 顶部提示连接失败 / 看板为空 | 检查 `DoltHost` / `DoltPort` / `DoltUser` / `DoltPassword` / `DoltDatabase`（密码留空时看 `BEADS_DOLT_PASSWORD`），用「测试连接」验证 |
| 提示不是 beads 库 | 目标库缺少 beads 表结构，确认 `DoltDatabase` 或先用 bd 在该库初始化 |
| `LockBusy` / 写操作繁忙 | 行锁被其他客户端持有；等待重试，避免并发写同一条任务 |
| 写操作被拒绝且提示只读 | 关闭只读模式，或当前处于演示数据 / 「全部项目」聚合视图 |
| 界面无任务但库里明明有 | 检查当前选中的项目，以及任务是否被打了 `archived` 归档标签 |
| 启动异常 | 查看 `%APPDATA%\Bd.Ui\logs\bd-ui.log` |

## 架构

分层、数据边界、并发模型与测试策略见 [docs/architecture.md](docs/architecture.md)。
