using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Config;
using Bd.Core.Domain;

namespace Bd.Core.Services;

/// <summary>
/// 内置演示数据的唯一来源：bd 缺失或显式开启演示模式时，用这份数据填满看板、
/// Epics、依赖图与详情抽屉，使无 bd 环境也能浏览全部界面。演示存储只读。
/// </summary>
public static class DemoDataProvider
{
    public const string BackendName = "demo";

    public const string Version = "demo";

    public const string ReadOnlyReason = "演示数据不支持写入：当前未连接 bd 仓库，请在设置中配置仓库与 bd 可执行文件后重试。";

    /// <summary>配置显式要求演示模式（DemoMode=true 或 StorageBackend=demo）。</summary>
    public static bool IsDemoRequested(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.DemoMode
            || string.Equals(config.StorageBackend, BackendName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断 bd 可执行文件是否存在：带路径时看文件，裸文件名时搜 PATH。</summary>
    public static bool IsBdAvailable(string? bdBinaryPath)
    {
        var candidate = string.IsNullOrWhiteSpace(bdBinaryPath) ? "bd" : bdBinaryPath.Trim();

        if (candidate.Contains(Path.DirectorySeparatorChar)
            || candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(candidate) || File.Exists(candidate + ".exe");
        }

        var names = OperatingSystem.IsWindows()
            ? new[] { candidate, candidate + ".exe", candidate + ".cmd", candidate + ".bat" }
            : [candidate];

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return pathVariable
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(directory => names.Any(name => File.Exists(Path.Combine(directory, name))));
    }

    /// <summary>演示模式或找不到 bd 时回退到内置演示数据，避免首启即报错。</summary>
    public static bool ShouldUseDemoData(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return IsDemoRequested(config) || !IsBdAvailable(config.BdBinaryPath);
    }

    /// <summary>创建只读的演示存储；任何写操作都会带着 <see cref="ReadOnlyReason"/> 被拒绝。</summary>
    public static IBdStore CreateStore() => new ReadOnlyGuard(new DemoBdStore(), ReadOnlyReason);

    public static IReadOnlyList<Bead> CreateSeedBeads()
    {
        var now = DateTimeOffset.UtcNow;
        var api = now.AddMinutes(-40);
        var bug = now.AddMinutes(-25);

        return
        [
            new Bead
            {
                Id = "demo-epic",
                Title = "演示史诗：发布 1.0",
                Description = "内置演示数据，覆盖看板分列、Epic 进度、依赖图与详情抽屉。",
                StatusRaw = "open",
                PriorityValue = 1,
                TypeRaw = "epic",
                Owner = "sevencat",
                CreatedBy = "sevencat",
                CreatedAt = now.AddDays(-12),
                UpdatedAt = now.AddHours(-2),
                Labels = ["demo", "epic"]
            },
            new Bead
            {
                Id = "demo-api",
                Title = "实现 REST API 分页",
                Description = "为列表接口补齐游标分页，并补充契约测试。",
                StatusRaw = "in_progress",
                PriorityValue = 1,
                TypeRaw = "task",
                Owner = "alice",
                CreatedBy = "sevencat",
                CreatedAt = now.AddDays(-9),
                UpdatedAt = api,
                ParentId = "demo-epic",
                Labels = ["demo", "api"],
                CommentCount = 2
            },
            new Bead
            {
                Id = "demo-ui",
                Title = "完成设置界面校验",
                Description = "仓库路径、bd 路径与轮询间隔需要即时校验并给出可操作提示。",
                StatusRaw = "open",
                PriorityValue = 2,
                TypeRaw = "task",
                Owner = "bob",
                CreatedBy = "bob",
                CreatedAt = now.AddDays(-7),
                UpdatedAt = now.AddHours(-3),
                ParentId = "demo-epic",
                Labels = ["demo", "ui"]
            },
            new Bead
            {
                Id = "demo-docs",
                Title = "编写用户手册",
                Description = "介绍安装、配置、看板操作与常见问题。",
                StatusRaw = "closed",
                PriorityValue = 3,
                TypeRaw = "task",
                Owner = "carol",
                CreatedBy = "carol",
                CreatedAt = now.AddDays(-6),
                UpdatedAt = now.AddDays(-1),
                ParentId = "demo-epic",
                Labels = ["demo", "docs"]
            },
            new Bead
            {
                Id = "demo-bug",
                Title = "看板拖拽后偶发回滚",
                Description = "移列请求失败时卡片应回到原列，但当前偶发保留在新列。",
                StatusRaw = "open",
                PriorityValue = 0,
                TypeRaw = "bug",
                Owner = "alice",
                CreatedBy = "bob",
                CreatedAt = now.AddDays(-2),
                UpdatedAt = bug,
                Labels = ["demo", "bug"],
                CommentCount = 1,
                Dependencies =
                [
                    new DependencyLink
                    {
                        TargetId = "demo-api",
                        Title = "实现 REST API 分页",
                        TargetStatusRaw = "in_progress",
                        TargetTypeRaw = "task",
                        DependencyTypeRaw = BdEnumMaps.ToBdString(DependencyType.Blocks)
                    }
                ],
                DependencyCount = 1
            },
            new Bead
            {
                Id = "demo-blocked",
                Title = "发布前性能回归",
                Description = "等待 API 分页合入后再跑全量回归。",
                StatusRaw = "blocked",
                PriorityValue = 1,
                TypeRaw = "task",
                Owner = "sevencat",
                CreatedBy = "sevencat",
                CreatedAt = now.AddDays(-4),
                UpdatedAt = now.AddHours(-5),
                Labels = ["demo", "release"],
                Dependencies =
                [
                    new DependencyLink
                    {
                        TargetId = "demo-api",
                        Title = "实现 REST API 分页",
                        TargetStatusRaw = "in_progress",
                        TargetTypeRaw = "task",
                        DependencyTypeRaw = BdEnumMaps.ToBdString(DependencyType.Blocks)
                    }
                ],
                DependencyCount = 1
            },
            new Bead
            {
                Id = "demo-feature",
                Title = "看板列内快速排序",
                Description = "支持按优先级、更新时间与标题切换排序。",
                StatusRaw = "open",
                PriorityValue = 2,
                TypeRaw = "feature",
                Owner = "bob",
                CreatedBy = "bob",
                CreatedAt = now.AddDays(-5),
                UpdatedAt = now.AddHours(-8),
                Labels = ["demo", "board"]
            },
            new Bead
            {
                Id = "demo-chore",
                Title = "补齐 CI 质量门禁",
                Description = "格式检查、单测与发布冒烟串成一条流水线。",
                StatusRaw = "deferred",
                PriorityValue = 3,
                TypeRaw = "chore",
                Owner = "carol",
                CreatedBy = "carol",
                CreatedAt = now.AddDays(-8),
                UpdatedAt = now.AddDays(-3),
                Labels = ["demo", "ci"]
            },
            new Bead
            {
                Id = "demo-decision",
                Title = "选用 Avalonia 作为桌面 UI 框架",
                Description = "跨平台、可测试、与 xunit headless 兼容。",
                StatusRaw = "closed",
                PriorityValue = 2,
                TypeRaw = "decision",
                Owner = "sevencat",
                CreatedBy = "sevencat",
                CreatedAt = now.AddDays(-14),
                UpdatedAt = now.AddDays(-5),
                Labels = ["demo"]
            },
            new Bead
            {
                Id = "demo-spike",
                Title = "评估 Dolt 直连读取",
                Description = "比较 CLI 与直连的延迟和一致性问题。",
                StatusRaw = "open",
                PriorityValue = 3,
                TypeRaw = "spike",
                Owner = "alice",
                CreatedBy = "sevencat",
                CreatedAt = now.AddDays(-3),
                UpdatedAt = now.AddHours(-10),
                Labels = ["demo", "storage"],
                DependencyCount = 1,
                Dependencies =
                [
                    new DependencyLink
                    {
                        TargetId = "demo-api",
                        Title = "实现 REST API 分页",
                        TargetStatusRaw = "in_progress",
                        TargetTypeRaw = "task",
                        DependencyTypeRaw = BdEnumMaps.ToBdString(DependencyType.Related)
                    }
                ]
            },
            new Bead
            {
                Id = "demo-old",
                Title = "初始化项目骨架",
                Description = "已完成的历史任务，用于展示 Done 列。",
                StatusRaw = "closed",
                PriorityValue = 4,
                TypeRaw = "task",
                Owner = "sevencat",
                CreatedBy = "sevencat",
                CreatedAt = now.AddDays(-20),
                UpdatedAt = now.AddDays(-18),
                Labels = ["demo", BdLabels.Archived]
            }
        ];
    }

    public static IReadOnlyList<Comment> CreateSeedComments()
    {
        var now = DateTimeOffset.UtcNow;

        return
        [
            new Comment
            {
                Id = "demo-c1",
                IssueId = "demo-api",
                Author = "alice",
                Text = "游标分页已实现，正在补契约测试。",
                CreatedAt = now.AddHours(-3)
            },
            new Comment
            {
                Id = "demo-c2",
                IssueId = "demo-api",
                Author = "sevencat",
                Text = "记得覆盖空结果与非法游标。",
                CreatedAt = now.AddHours(-2)
            },
            new Comment
            {
                Id = "demo-c3",
                IssueId = "demo-bug",
                Author = "bob",
                Text = "复现步骤已附在描述里，优先修这个。",
                CreatedAt = now.AddHours(-1)
            },
            new Comment
            {
                Id = "demo-c4",
                IssueId = "demo-epic",
                Author = "sevencat",
                Text = "文档与设置界面完成后即可发版。",
                CreatedAt = now.AddDays(-1)
            }
        ];
    }

    /// <summary>内存演示存储：实现全部读语义，写操作统一以只读错误拒绝。</summary>
    private sealed class DemoBdStore : IBdStore
    {
        private readonly IReadOnlyList<Bead> _beads = CreateSeedBeads();
        private readonly IReadOnlyList<Comment> _comments = CreateSeedComments();

        public string BackendName => DemoDataProvider.BackendName;

        public Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<Bead> result = _beads;

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                result = result.Where(bead => string.Equals(bead.StatusRaw, query.Status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.Type))
            {
                result = result.Where(bead => string.Equals(bead.TypeRaw, query.Type, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.Priority))
            {
                result = result.Where(bead => string.Equals(bead.PriorityValue.ToString(), query.Priority, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.Label))
            {
                result = result.Where(bead => bead.HasLabel(query.Label));
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var keyword = query.Search.Trim();
                result = result.Where(bead =>
                    bead.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || bead.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (bead.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (query.Limit > 0)
            {
                result = result.Take(query.Limit);
            }

            return Task.FromResult<IReadOnlyList<Bead>>([.. result]);
        }

        public Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Bead>>(
                [.. _beads.Where(bead => bead.Status == BeadStatus.Open && !bead.IsOpenBlocker)]);

        public Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Bead>>(
                [.. _beads.Where(bead => bead.Status == BeadStatus.Blocked || bead.IsOpenBlocker)]);

        public Task<Bead> GetAsync(string id, CancellationToken cancellationToken)
        {
            var bead = _beads.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            return bead is null
                ? Task.FromException<Bead>(BdException.NotFound(id))
                : Task.FromResult(bead);
        }

        public Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken)
        {
            var bead = _beads.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(bead?.Dependencies ?? (IReadOnlyList<DependencyLink>)[]);
        }

        public Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Comment>>(
                [.. _comments
                    .Where(comment => string.Equals(comment.IssueId, id, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(comment => comment.CreatedAt)]);

        public Task<string> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult(DemoDataProvider.Version);

        public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DoctorReport
            {
                Ok = true,
                BdVersion = DemoDataProvider.Version,
                Summary = "演示数据模式：未连接 bd CLI，仅供界面浏览"
            });

        public Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken) =>
            Reject<string>();

        public Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken) => Reject();

        public Task CloseAsync(string id, string? reason, CancellationToken cancellationToken) => Reject();

        public Task DeleteAsync(string id, CancellationToken cancellationToken) => Reject();

        public Task AddCommentAsync(string id, string text, CancellationToken cancellationToken) => Reject();

        public Task AddLabelAsync(string id, string label, CancellationToken cancellationToken) => Reject();

        public Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken) => Reject();

        public Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken) => Reject();

        public Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken) => Reject();

        private static Task<T> Reject<T>() => Task.FromException<T>(BdException.ReadOnly(ReadOnlyReason));

        private static Task Reject() => Task.FromException(BdException.ReadOnly(ReadOnlyReason));
    }
}
