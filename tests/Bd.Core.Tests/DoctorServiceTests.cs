using Bd.Core.BdCli;
using Bd.Core.Services;
using Bd.Core.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class DoctorServiceTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "bd-ui-doctor", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Missing_binary_returns_actionable_message()
    {
        var runner = new FakeBdProcessRunner().MapFailure("version", "executable file not found in $PATH");
        var service = new DoctorService(new BdCliStore(runner, _repo));

        var report = await service.CheckAsync(_repo, CancellationToken.None);

        report.Ok.Should().BeFalse();
        report.Issues.Should().Contain(text => text.Contains("bd", StringComparison.OrdinalIgnoreCase));
        report.Summary.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Healthy_environment_passes()
    {
        Directory.CreateDirectory(Path.Combine(_repo, ".beads"));
        var runner = new FakeBdProcessRunner()
            .Map("version", _ => new BdProcessResult { ExitCode = 0, StdOut = "bd version 1.3.4 (abc)" })
            .MapJson("doctor", """{"data":{"status":"ok"},"schema_version":1}""");
        var service = new DoctorService(new BdCliStore(runner, _repo));

        var report = await service.CheckAsync(_repo, CancellationToken.None);

        report.Ok.Should().BeTrue();
        report.BdVersion.Should().Be("1.3.4");
        report.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_beads_directory_is_reported()
    {
        Directory.CreateDirectory(_repo);
        var runner = new FakeBdProcessRunner()
            .Map("version", _ => new BdProcessResult { ExitCode = 0, StdOut = "bd version 1.3.4" })
            .MapJson("doctor", """{"data":{},"schema_version":1}""");
        var service = new DoctorService(new BdCliStore(runner, _repo));

        var report = await service.CheckAsync(_repo, CancellationToken.None);

        report.Ok.Should().BeFalse();
        report.Issues.Should().Contain(text => text.Contains(".beads", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Old_version_is_rejected()
    {
        Directory.CreateDirectory(Path.Combine(_repo, ".beads"));
        var runner = new FakeBdProcessRunner()
            .Map("version", _ => new BdProcessResult { ExitCode = 0, StdOut = "bd version 0.9.9" })
            .MapJson("doctor", """{"data":{},"schema_version":1}""");
        var service = new DoctorService(new BdCliStore(runner, _repo));

        var report = await service.CheckAsync(_repo, CancellationToken.None);

        report.Ok.Should().BeFalse();
        report.Issues.Should().Contain(text => text.Contains("版本过低", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }
}
