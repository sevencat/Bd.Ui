using Bd.Core.Config;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class AppConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bd-ui-tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "config.json");

    [Fact]
    public async Task Missing_file_returns_defaults()
    {
        var store = new JsonAppConfigStore(FilePath);

        var config = await store.LoadAsync(CancellationToken.None);

        config.BdBinaryPath.Should().Be("bd");
        config.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Round_trips_and_normalizes_values()
    {
        var store = new JsonAppConfigStore(FilePath);
        var config = new AppConfig
        {
            RepoPath = "F:\\code\\repo",
            HumanActor = "sevencat",
            PollSeconds = 0,
            Theme = " ",
            HumanAllowlist = [" alice ", "alice", ""]
        };

        await store.SaveAsync(config, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        loaded.RepoPath.Should().Be("F:\\code\\repo");
        loaded.PollSeconds.Should().Be(1);
        loaded.Theme.Should().Be("Dark");
        loaded.HumanAllowlist.Should().Equal("alice");
    }

    [Fact]
    public async Task Corrupted_file_falls_back_to_default_and_keeps_backup()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{ not json");
        var store = new JsonAppConfigStore(FilePath);

        var config = await store.LoadAsync(CancellationToken.None);

        config.Should().Be(AppConfig.Default);
        Directory.GetFiles(_directory, "config.json.corrupt-*").Should().ContainSingle();
    }

    [Fact]
    public async Task Save_creates_directory_and_leaves_no_temp_files()
    {
        var store = new JsonAppConfigStore(FilePath);

        await store.SaveAsync(new AppConfig { RepoPath = "x" }, CancellationToken.None);
        await store.SaveAsync(new AppConfig { RepoPath = "y" }, CancellationToken.None);

        File.Exists(FilePath).Should().BeTrue();
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Unsupported_storage_backend_falls_back_to_cli()
    {
        new AppConfig { StorageBackend = "dolt" }.Normalize().StorageBackend.Should().Be("dolt");
        new AppConfig { StorageBackend = " DOLT " }.Normalize().StorageBackend.Should().Be("dolt");
        new AppConfig { StorageBackend = "unknown" }.Normalize().StorageBackend.Should().Be("cli");
        new AppConfig { StorageBackend = "" }.Normalize().StorageBackend.Should().Be("cli");
        new AppConfig { StorageBackend = "DEMO" }.Normalize().StorageBackend.Should().Be("demo");
    }

    [Fact]
    public void Dolt_settings_are_trimmed_and_clamped()
    {
        var config = new AppConfig
        {
            DoltHost = "  10.0.0.1  ",
            DoltPort = 0,
            DoltUser = " ",
            DoltDatabase = " king_fh2sharp ",
            StorageBackend = "dolt"
        }.Normalize();

        config.DoltHost.Should().Be("10.0.0.1");
        config.DoltPort.Should().Be(1);
        config.DoltUser.Should().Be(DoltDefaults.User);
        config.DoltDatabase.Should().Be("king_fh2sharp");
        config.UsesDolt.Should().BeTrue();
    }

    [Fact]
    public void Human_attribution_matches_allowlist_case_insensitively()
    {
        var config = new AppConfig { HumanActor = "Sevencat", HumanAllowlist = ["alice"] };

        config.IsHuman("sevencat", []).Should().BeTrue();
        config.IsHuman("ALICE", []).Should().BeTrue();
        config.IsHuman("agent-x", ["agent-x"]).Should().BeTrue();
        config.IsHuman("agent-y", []).Should().BeFalse();
        config.IsHuman(null, []).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
