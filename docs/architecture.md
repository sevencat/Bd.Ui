# 架构说明

## 目标与边界

Bd.Ui 是 bd（beads）的桌面视图，不是 bd 的替代实现：

- **唯一真相源是 Dolt 服务器上的 beads 库。** 应用不维护自己的任务数据库，也不做 schema 迁移。
- **所有读写都直连 Dolt（MySQL 协议）。** `IBdStore` 是唯一数据入口，SQL 细节被限制在 `Bd.Core/Dolt` 内；应用不启动 `bd` 子进程，也不依赖本地 `.beads` 目录。
- **没有本地缓存落盘。** 界面状态只在内存中，刷新失败时保留上一次快照用于展示。
- **演示模式是只读的。** 内置数据用于无服务器环境下浏览全部界面，任何写操作都会被拒绝。

## 分层

```
Bd.App (Avalonia)
  ├─ Views / Dialogs / Controls / Converters / Styles
  ├─ Composition/ServiceRegistration（组合根）
  └─ App.axaml.cs（文件日志 + 全局异常捕获）
       │
Bd.Core
  ├─ ViewModels   Shell / Board / BoardTree / Epics / Settings / BeadDetail / Editor
  ├─ Services     BoardService / BeadDetailService / EpicProgressService
  │               BeadWriteService / ArchiveService / AttributionService
  │               RefreshCoordinator / ToastService / AppSession / DoctorService
  │               DemoDataProvider / ReadOnlyGuard
  ├─ Abstractions IBdStore / StoreProvider / BdException / 请求与查询模型
  ├─ Dolt         DoltConnectionFactory / DoltConnectionSettings / DoltStore.* / CompositeBdStore / ProjectCatalog
  ├─ Domain       Bead / Comment / Dependency / DoctorReport
  ├─ Config       AppConfig / JsonAppConfigStore
  └─ Logging      FileLoggerProvider

Bd.Testing            测试替身（InMemoryBdStore / FaultyBdStore）
tests/Bd.Core.Tests   单元、集成、ViewModel、日志测试
tests/Bd.App.Tests    Avalonia.Headless 界面测试
```

依赖方向只能自上而下：`App → Core.ViewModels → Core.Services → Core.Abstractions → Core.Dolt`。`Domain` 不依赖任何上层；`Bd.Testing` 与测试工程只引用 `Bd.Core`。

## 目录职责

| 路径 | 职责 |
| --- | --- |
| `src/Bd.App/Composition/ServiceRegistration.cs` | 唯一组合根：配置存储、存储工厂、服务与 ViewModel 的装配 |
| `src/Bd.App/App.axaml.cs` | 应用生命周期、文件日志、UI 线程 / 未观察任务 / 应用域异常 |
| `src/Bd.Core/Dolt/DoltStore.*.cs` | SQL 拼装、行映射、错误码映射、写事务与乐观锁 |
| `src/Bd.Core/Services/*Service.cs` | 读模型组装与业务规则，不直接触碰连接 |
| `src/Bd.Core/ViewModels/*` | 展示状态与命令，不拼 SQL、不碰连接串 |
| `src/Bd.Core/Config/*` | 配置模型、校验归一化、原子写与损坏回退 |

## 启动与数据流

```
Program.Main
  → App.OnFrameworkInitializationCompleted
      → 读取 config.json（失败回退默认值）
      → 启动 FileLoggerProvider
      → ServiceRegistration.Build()
      → MainWindow.Opened → ShellViewModel.InitializeAsync()
            → AppSession.InitializeAsync()
                 → 加载配置 + Normalize
                 → storeFactory(config) → DemoDataProvider 或 DoltStore / CompositeBdStore(+ReadOnlyGuard)
                 → StoreProvider.Configure(store)
            → ShellViewModel.ApplyConfig / RefreshAll
```

读路径：

```
BoardViewModel / EpicsViewModel
  → BoardService / EpicProgressService
      → IBdStore（StoreProvider 代理）
          → DoltStore → MySqlConnector → dolt sql-server（MySQL 协议）
```

写路径：

```
ViewModel 命令
  → BeadWriteService / BoardViewModel.MoveAsync
      → IBdStore 写方法
          → ReadOnlyGuard（只读时立即拒绝）
          → DoltStore 写事务（行级 UPDATE + row_lock 乐观锁）
      → 成功后立即刷新；失败保留/回滚 UI 状态并 toast
```

### 存储选择

`ServiceRegistration.CreateStore` 的规则：

1. `DemoMode=true`：内置演示数据（只读）。
2. `DoltAggregateAll=true`：`CompositeBdStore` 合并服务器上所有 beads 库为「全部项目」，只读。
3. 其余：`DoltStore` 直连配置的库；若 `ReadOnly=true` 再套一层 `ReadOnlyGuard`。

连接参数来自 `AppConfig.Dolt*`；密码优先用 `DoltPassword`（设置页可改，明文存于 config.json），留空时回退环境变量 `BEADS_DOLT_PASSWORD`。设置保存后 `AppSession.ApplyToRuntime` 通过 `StoreProvider.Configure` 原地替换存储，不重建 DI 容器，因此各 ViewModel 持有的 `StoreProvider` 引用始终有效。

## 关键组件

| 组件 | 说明 |
| --- | --- |
| `IBdStore` | 读写契约；同时提供 `BackendName`，用于区分 `dolt` / `demo` |
| `DoltConnectionFactory` | 构建 MySqlConnector 连接（超时、TLS、连接池），统一错误码映射 |
| `DoltStore` | 读查询与 SQL 解析（`DoltStore.Read.cs`）、写入事务与乐观锁（`DoltStore.Write.cs`） |
| `CompositeBdStore` | 「全部项目」只读视图：按 `ProjectCatalog` 枚举库并带上项目前缀 |
| `BdException` | 统一错误码：`NotBeadsDatabase` / `NotFound` / `LockBusy` / `SchemaMismatch` / `Timeout` / `Canceled` / `ReadOnly` / `Connection` |
| `ReadOnlyGuard` | 装饰器：读透传，九类写操作直接返回 `ReadOnly` 错误 |
| `DoctorService` | 库内自检：`local_metadata` 版本、schema、任务数量；演示模式短路 |
| `DemoDataProvider` | 11 条演示任务、评论与依赖；与真实数据走完全相同的读服务 |
| `RefreshCoordinator` | 按需刷新（手动 / 写后 / 配置变更）串行化，`RefreshLock` 防止重入 |
| `AttributionService` | 依据创建者与 human allowlist 标注 human / agent |
| `StoreProvider` | 可替换内层实现的代理，解决“配置变更后换存储”问题 |

## 线程与并发

- 所有 ViewModel 状态只在 UI 线程修改；服务层异步方法不触碰 UI 对象。
- 不自动刷新：只在用户点「刷新」、写操作落地、保存设置后重新读库；`RefreshCoordinator` 保证单次刷新未完成时不会并发进入第二次。
- 写操作在 `DoltStore` 内以事务串行提交，并带 `row_lock` 乐观锁；冲突映射为 `LockBusy`。
- 设置保存是“先落盘、再替换运行时”，替换期间旧存储仍可完成在途请求。
- 取消是正常路径：`OperationCanceledException` 不弹错误 toast，也不改变当前快照。

## 渲染与 Win64Aio 约束

- 通过 `Sevencat.Avalonia.Win64Aio`（`12.2.0.1`，对应 Avalonia `12.2.0`）提供 Windows x64 运行时：
  `Program.cs` 走 `UseWin32().UseSkia().UseHarfBuzz().WithInterFont()`，Win32 组合为 `RenderingMode = AngleEgl`。
- 该包已把 Win32 / Skia / HarfBuzz / OpenGL / Metal / Vulkan / MicroCom 合并进 `Avalonia.Win64Aio.dll`；
  `SkiaSharp.dll` / `HarfBuzzSharp.dll` 则**必须保持独立程序集**（合并会破坏其 `[UnmanagedCallersOnly]`
  互操作桩，`Bitmap(Stream)` 等托管流解码会抛 `InvalidProgramException`），输出目录出现这两个 dll 是正常的。
- 仓库不直接引用 SkiaSharp / HarfBuzzSharp 原生库；发布为自包含单文件（`PublishSingleFile`、`IncludeNativeLibrariesForSelfExtract`、`PublishTrimmed=false`）。
- 主题使用 `Avalonia.Themes.Fluent2` 的 `Fluent2Theme`（随 Win64Aio 包提供，程序集身份保留）与 `Styles/Tokens.axaml`、`Styles/Controls.axaml`；不要额外引入 Fluent 主题包，也不要依赖 `avares://Bd.App`（资源前缀是 `avares://Bd.Ui`）。

## 测试策略

| 层级 | 方式 | 代表用例 |
| --- | --- | --- |
| SQL 集成 | 真实 Dolt 服务器（需 `BEADS_DOLT_PASSWORD` 或默认口令；写测试另需 `BD_UI_ENABLE_WRITE_TESTS=1`，否则跳过） | 列表/详情读回、创建→更新→关闭、事件与依赖落库 |
| 配置与规则 | 纯内存对象 | 归一化、损坏回退、doctor 诊断规则 |
| ViewModel | `InMemoryBdStore` / `FaultyBdStore` 注入故障 | 移列回滚、保存失败保留编辑、归档只读、树状展开状态 |
| 界面 | `Avalonia.Headless.XUnit` | 布局标签正确、详情抽屉、快捷键保护、图标资源 |
| 日志 | 临时目录 | 堆栈落盘、滚动、过期清理 |

质量门禁：`pwsh scripts/ci.ps1`（restore → build `-warnaserror` → test → publish 冒烟），CI 使用同一脚本。

## 打包与版本

1. `scripts/set-version.ps1 <semver>` 写入 `Directory.Build.props` 的 `<Version>`，构建后进入 assembly 与「设置 → 关于」。
2. `scripts/publish.ps1` 生成 `artifacts/publish/Bd.Ui.exe`（自包含单文件）。
3. `scripts/package.ps1` 复制发布产物、追加 README、压缩为 `Bd.Ui-<version>-win-x64.zip` 并生成 `.sha256`。
4. 发布冒烟通过环境变量 `BD_UI_SMOKE_EXIT_SECONDS` 让应用自动正常退出，避免 CI 残留 GUI 进程。

## 已知限制

- 单窗口、单库；「全部项目」聚合视图只读，不支持跨库拖拽。
- 只支持 Dolt 服务器直连，不支持 MySQL 之外的协议；无本地离线库。
- 未实现 Focus 视图与使用统计。
- 拖到 Blocked 列不会写入状态，因为阻塞语义由依赖关系决定。
- 演示数据只读，且不会持久化任何修改。
