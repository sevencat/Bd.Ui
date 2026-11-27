using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Dolt;
using Bd.Core.Services;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>
/// 项目切换的机制验证：<see cref="AppSession.ApplyAsync"/> 换库后
/// <see cref="StoreProvider"/> 必须指向新库的数据。未设置密码时跳过。
/// </summary>
public sealed class ProjectSwitchTests
{
    [Fact]
    public async Task Applying_new_database_replaces_the_store()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DoltConnectionFactory.PasswordEnvironmentVariable)))
        {
            return;
        }

        var configStore = new MemoryConfigStore(new AppConfig
        {
            StorageBackend = StorageBackends.Dolt,
            DoltDatabase = "king_bdui"
        });

        var store = new StoreProvider();
        var factory = new DoltConnectionFactory();
        var session = new AppSession(
            configStore,
            config => new ReadOnlyGuard(
                new DoltStore(factory, DoltConnectionSettings.FromConfig(config)),
                "测试用只读包装"),
            store,
            new AttributionService(AppConfig.Default));

        await session.InitializeAsync(CancellationToken.None);
        var before = await store.ListAsync(new BeadListQuery { Limit = 5 }, CancellationToken.None);
        before.Should().NotBeEmpty().And.OnlyContain(static bead => bead.Id.StartsWith("Bd_Ui-", StringComparison.Ordinal));

        await session.ApplyAsync(session.Config with { DoltDatabase = "king_fh2sharp" }, CancellationToken.None);

        configStore.Saved!.DoltDatabase.Should().Be("king_fh2sharp");
        store.BackendName.Should().Be(DoltStore.BackendNameValue);

        var after = await store.ListAsync(new BeadListQuery { Limit = 5 }, CancellationToken.None);
        after.Should().NotBeEmpty()
            .And.OnlyContain(static bead => bead.Id.StartsWith("FHeroes2Sharp-", StringComparison.Ordinal));
        after.Select(static bead => bead.Id).Should().NotIntersectWith(before.Select(static bead => bead.Id));
    }

    private sealed class MemoryConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig Initial { get; } = initial;

        public AppConfig? Saved { get; private set; }

        public string FilePath => "memory://bd-ui/config.json";

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Initial);

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            Saved = config;
            return Task.CompletedTask;
        }
    }
}
