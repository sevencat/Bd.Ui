using System.Data;
using Bd.Core.Dolt;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class ProjectCatalogTests
{
    [Fact]
    public void Projects_query_is_a_single_union_with_parameters()
    {
        var sql = ProjectCatalog.BuildProjectsSql(["king_bdui", "king_fh2sharp"]);

        sql.Should().Contain("UNION ALL")
            .And.Contain("@p0")
            .And.Contain("@p1")
            .And.Contain("`king_bdui`.`issues`")
            .And.Contain("`king_fh2sharp`.`config`")
            .And.Contain("`key` = 'issue_prefix'")
            .And.Contain("`key` = 'bd_version'");
        sql.Split("UNION ALL").Should().HaveCount(2);
    }

    [Fact]
    public void Identifiers_are_backtick_escaped()
    {
        ProjectCatalog.QuoteIdentifier("king_bdui").Should().Be("`king_bdui`");
        ProjectCatalog.QuoteIdentifier("we`ird").Should().Be("`we``ird`");
    }

    [Fact]
    public void Project_label_shows_prefix_database_and_count()
    {
        new ProjectInfo("king_bdui", "Bd_Ui", 6, "1.3.4").Label.Should().Be("Bd_Ui · king_bdui (6)");
        new ProjectInfo("nwms_nwms", null, 0, null).Label.Should().Be("nwms_nwms (0)");
        new ProjectInfo("nwms_nwms", " ", 0, null).Label.Should().Be("nwms_nwms (0)");
    }

    [Fact]
    public void Row_mapping_reads_all_four_columns()
    {
        using var table = new DataTable();
        table.Columns.Add("database_name", typeof(string));
        table.Columns.Add("bead_count", typeof(long));
        table.Columns.Add("issue_prefix", typeof(string));
        table.Columns.Add("bd_version", typeof(string));
        table.Rows.Add("king_fh2sharp", 263L, "FHeroes2Sharp", "1.3.4");
        table.Rows.Add("test", DBNull.Value, DBNull.Value, DBNull.Value);

        using var reader = new DataTableReader(table);
        reader.Read();
        var project = ProjectCatalog.MapProject(reader);

        project.Database.Should().Be("king_fh2sharp");
        project.BeadCount.Should().Be(263);
        project.Prefix.Should().Be("FHeroes2Sharp");
        project.BdVersion.Should().Be("1.3.4");

        reader.Read();
        var empty = ProjectCatalog.MapProject(reader);
        empty.BeadCount.Should().Be(0);
        empty.Prefix.Should().BeNull();
        empty.BdVersion.Should().BeNull();
    }

    [Fact]
    public async Task Live_catalog_lists_real_projects()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DoltConnectionFactory.PasswordEnvironmentVariable)))
        {
            return;
        }

        var catalog = new ProjectCatalog(new DoltConnectionFactory());
        var projects = await catalog.ListAsync(
            new DoltConnectionSettings
            {
                Host = "1.15.245.172",
                Port = 7050,
                User = "nwms",
                Database = "king_bdui"
            },
            CancellationToken.None);

        projects.Should().NotBeEmpty();
        projects.Select(static project => project.Database).Should().Contain(["king_bdui", "king_fh2sharp", "nwms_nwms"]);
        projects.Should().OnlyContain(static project => !string.IsNullOrWhiteSpace(project.Database));

        var bdui = projects.Single(static project => project.Database == "king_bdui");
        bdui.Prefix.Should().Be("Bd_Ui");
        bdui.BdVersion.Should().Be("1.3.4");
        bdui.BeadCount.Should().BeGreaterThan(0);

        projects.Single(static project => project.Database == "king_fh2sharp").Prefix.Should().Be("FHeroes2Sharp");

        // 目录按条数倒序，便于下拉里先看到活跃项目。
        projects.Select(static project => project.BeadCount).Should().BeInDescendingOrder();
    }
}
