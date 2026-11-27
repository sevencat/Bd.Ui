using Bd.Core.Abstractions;
using Bd.Core.Services;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class DoctorServiceTests
{
    [Fact]
    public void Healthy_dolt_library_passes()
    {
        var report = DoctorService.Evaluate(new DoctorReport
        {
            Ok = true,
            BdVersion = "1.3.4",
            SchemaVersion = 5,
            RepoPath = "dolt://localhost:3307/beads"
        });

        report.Ok.Should().BeTrue();
        report.Issues.Should().BeEmpty();
        report.Summary.Should().Contain("Dolt 自检通过").And.Contain("1.3.4");
    }

    [Fact]
    public void Missing_version_record_is_reported()
    {
        var report = DoctorService.Evaluate(new DoctorReport { Ok = true, BdVersion = "", SchemaVersion = 5 });

        report.Ok.Should().BeFalse();
        report.Issues.Should().Contain(text => text.Contains("local_metadata", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_schema_version_is_reported()
    {
        var report = DoctorService.Evaluate(new DoctorReport { Ok = true, BdVersion = "1.2.0", SchemaVersion = 0 });

        report.Ok.Should().BeFalse();
        report.Issues.Should().Contain(text => text.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Demo_store_skips_version_checks()
    {
        var service = new DoctorService(DemoDataProvider.CreateStore());

        var report = await service.CheckAsync(CancellationToken.None);

        report.Ok.Should().BeTrue();
        report.Issues.Should().BeEmpty();
    }
}
