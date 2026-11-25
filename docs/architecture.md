# 架构说明

## 目标与边界

Bd.Ui 是 bd（beads）的桌面视图，不是 bd 的替代实现：

- **唯一真相源是 bd 仓库。** 应用不直接读写 `.beads/dolt`，不维护自己的任务数据库，也不做 schema 迁移。
- **所有读写都经过 `bd` CLI。** `IBdStore` 是唯一数据入口，CLI 细节被限制在 `Bd.Core/BdCli` 内。
- **没有本地缓存落盘。** 界面状态只在内存中，刷新失败时保留上一次快照用于展示。
- **演示模式是只读的。** 内置数据用于无 bd 环境下浏览全部界面，任何写操作都会被拒绝。

## 分层

```
Bd.App (Avalonia)
  ├─ Views / Dialogs / Controls / Converters / Styles
  ├─ Composition/ServiceRegistration（组合根）
  └─ App.axaml.cs（文件日志 + 全局异常捕获）
        │
Bd.Core
  ├─ ViewModels   Shell / Board / Epics / Graph / Settings / BeadDetail / Editor
  ├─ Services     BoardService / BeadDetailService / EpicProgressService / DependencyService
  │               BeadWriteService / ArchiveService / AttributionService
  │               RefreshCoordinator / ToastService / AppSession
  │               DemoDataProvider / ReadOnlyGuard
  ├─ Abstractions IBdStore / StoreProvider / 请求与查询模型
  ├─ BdCli        BdProcessRunner / BdJsonParser / BdWriteGate / BdException / BdCliStore
  ├─ Domain       Bead / Comment / Dependency / DoctorReport
  ├─ Config       AppConfig / JsonAppConfigStore
  ├─ Filters      BoardFilter 与分列规则
  └─ Logging      FileLoggerProvider

Bd.Testing            测试替身（InMemoryBdStore / FaultyBdStore）
tests/Bd.Core.Tests   单元、合同、ViewModel、日志测试
tests/Bd.App.Tests    Avalonia.Headless 界面测试
```

依赖方向只能自上而下：`App → Core.ViewModels → Core.Services → Core.Abstractions → Core.BdCli`。`Domain` 不依赖任何上层；`Bd.Testing` 与测试工程只引用 `Bd.Core`。

## 目录职责

| 路径 | 职责 |
| --- | --- |
| `src/Bd.App/Composition/ServiceRegistration.cs` | 唯一组合根：配置存储、存储工厂、服务与 ViewModel 的装配 |
| `src/Bd.App/App.axaml.cs` | 应用生命周期、文件日志、UI 线程 / 未观察任务 / 应用域异常 |
| `src/Bd.Core/BdCli/BdCliStore.*.cs` | bd 命令拼装、JSON 解析、错误码映射、写串行化 |
| `src/Bd.Core/Services/*Service.cs` | 读模型组装与业务规则，不直接启动进程 |
| `src/Bd.Core/ViewModels/*` | 展示状态与命令，不拼 bd 命令 |
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
                 → storeFactory(config) → DemoDataProvider 或 BdCliStore(+ReadOnlyGuard)
                 → StoreProvider.Configure(store)
            → ShellViewModel.ApplyConfig / RefreshAll
```

读路径：

```
BoardViewModel / EpicsViewModel / GraphViewModel
  → BoardService / EpicProgressService / DependencyService
      → IBdStore（StoreProvider 代理）
          → BdCliStore → BdProcessRunner → bd --json
```

写路径：

```
ViewModel 命令
  → BeadWriteService / BoardViewModel.MoveAsync
      → IBdStore 写方法
          → ReadOnlyGuard（只读时立即拒绝）
          → BdCliStore → BdWriteGate（单写者串行）→ bd CLI
      → 成功后立即刷新；失败保留/回滚 UI 状态并 toast
```

### 存储选择

`ServiceRegistration.CreateStore` 的规则：

1. `DemoMode=true` 或 `StorageBackend=demo`：内置演示数据。
2. 找不到 bd 可执行文件（裸文件名搜 PATH，含路径则查文件）：自动回退演示数据。
3. 其余：`BdCliStore`；若 `ReadOnly=true` 再套一层 `ReadOnlyGuard`。

设置保存后 `AppSession.ApplyToRuntime` 通过 `StoreProvider.Configure` 原地替换存储，不重建 DI 容器，因此各 ViewModel 持有的 `StoreProvider` 引用始终有效。

## 关键组件

| 组件 | 说明 |
| --- | --- |
| `IBdStore` | 读写契约；同时提供 `BackendName`，用于区分 `bd-cli` / `demo` |
| `BdProcessRunner` | 启动 `bd` 子进程，固定 `BD_JSON_ENVELOPE=1`，超时与取消，stderr 摘要 |
| `BdJsonParser` | 只解析已知字段，缺失字段降级为空值，避免 bd 小版本差异导致崩溃 |
| `BdWriteGate` | `SemaphoreSlim(1,1)` 串行化写命令；等待可取消，异常也会释放锁 |
| `BdException` | 统一错误码：`BinaryMissing` / `NotARepo` / `NotFound` / `LockBusy` / `Timeout` / `Canceled` / `ReadOnly` 等 |
| `ReadOnlyGuard` | 装饰器：读透传，九类写操作直接返回 `ReadOnly` 错误 |
| `DemoDataProvider` | 11 条演示任务、评论与依赖；与真实数据走完全相同的读服务 |
| `RefreshCoordinator` | 轮询 + 手动刷新合并，`RefreshLock` 防止重入，写后可强制失效 |
| `AttributionService` | 依据创建者与 human allowlist 标注 human / agent |
| `StoreProvider` | 可替换内层实现的代理，解决“配置变更后换存储”问题 |

## 线程与并发

- 所有 ViewModel 状态只在 UI 线程修改；服务层异步方法不触碰 UI 对象。
- 轮询由 `RefreshCoordinator` 驱动，单次刷新未完成时不会并发进入第二次。
- 写操作通过 `BdWriteGate` 串行，且与刷新共享同一 CancellationToken 生命周期。
- 设置保存是“先落盘、再替换运行时”，替换期间旧存储仍可完成在途请求。
- 取消是正常路径：`OperationCanceledException` 不弹错误 toast，也不改变当前快照。

## 渲染与 SimdAIO 约束

- 通过 `Sevencat.Avalonia.SimdAIO` 提供 Avalonia 运行时，`Program.cs` 使用 `UseSimdSkia`，Win32 组合固定 `RenderingMode = Software`。
- 仓库不直接引用 SkiaSharp / HarfBuzzSharp 原生库；发布为自包含单文件（`PublishSingleFile`、`IncludeNativeLibrariesForSelfExtract`、`PublishTrimmed=false`）。
- 主题使用 Avalonia 内置 `FluentTheme` 与 `Styles/Tokens.axaml`、`Styles/Controls.axaml`；不要额外引入 Fluent 主题包，也不要依赖 `avares://Bd.App`（资源前缀是 `avares://Bd.Ui`）。
- 自定义绘制（依赖图）只用 Avalonia 基础控件与画刷，不引入第三方图表库。

## 测试策略

| 层级 | 方式 | 代表用例 |
| --- | --- | --- |
| CLI 合同 | 真实 `bd` + 临时 `.beads` 仓库，`[BdFact]` 在缺少 bd 时跳过 | 列表/详情读回、创建→更新→关闭、错误码映射 |
| 迁移与解析 | 固定 JSON 样本 + `BdJsonParser` | 字段缺失、未知状态、空数组 |
| 并发 | 高并发写压测 | `BdWriteGate` 互斥、顺序、取消、异常释放 |
| ViewModel | `InMemoryBdStore` / `FaultyBdStore` 注入故障 | 移列回滚、保存失败保留编辑、归档只读 |
| 界面 | `Avalonia.Headless.XUnit` | 布局标签正确、详情抽屉、快捷键保护 |
| 日志 | 临时目录 | 堆栈落盘、滚动、过期清理 |

质量门禁：`pwsh scripts/ci.ps1`（restore → build `-warnaserror` → test → publish 冒烟），CI 使用同一脚本。

## 打包与版本

1. `scripts/set-version.ps1 <semver>` 写入 `Directory.Build.props` 的 `<Version>`，构建后进入 assembly 与「设置 → 关于」。
2. `scripts/publish.ps1` 生成 `artifacts/publish/Bd.Ui.exe`（自包含单文件）。
3. `scripts/package.ps1` 复制发布产物、追加 README、压缩为 `Bd.Ui-<version>-win-x64.zip` 并生成 `.sha256`。
4. 发布冒烟通过环境变量 `BD_UI_SMOKE_EXIT_SECONDS` 让应用自动正常退出，避免 CI 残留 GUI 进程。

## 已知限制

- 单窗口、单仓库；不支持 scotty 的多项目聚合与 Web 端。
- 存储后端仅实现 bd CLI 与内置演示数据；Dolt 直连（SQL）未实现，配置中的其他后端值会归一化为 `cli`。
- 未实现 Focus 视图与使用统计。
- 依赖图布局为固定分层布局，不提供手工拖拽节点位置。
- 拖到 Blocked 列不会写入 bd 状态，因为阻塞语义由依赖关系决定。
- 演示数据只读，且不会持久化任何修改。
