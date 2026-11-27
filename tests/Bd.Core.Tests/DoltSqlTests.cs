using Bd.Core.Abstractions;
using Bd.Core.Dolt;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class DoltSqlTests
{
    [Fact]
    public void List_query_without_filters_has_no_where_clause()
    {
        var (sql, parameters) = DoltSql.BuildListQuery(new BeadListQuery());

        sql.Should().Contain("FROM issues i");
        sql.Should().NotContain(" FROM issues i WHERE");
        sql.Should().Contain("ORDER BY i.priority, i.updated_at DESC, i.id");
        sql.Should().NotContain("LIMIT @limit");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void List_query_maps_every_filter_to_a_parameter()
    {
        var (sql, parameters) = DoltSql.BuildListQuery(new BeadListQuery
        {
            Status = " open ",
            Type = "task",
            Priority = "1",
            Label = "ui",
            Search = "board",
            Limit = 25
        });

        sql.Should().Contain("i.status = @status")
            .And.Contain("i.issue_type = @type")
            .And.Contain("i.priority = @priority")
            .And.Contain("l.label = @label")
            .And.Contain("i.id LIKE @search")
            .And.Contain("LIMIT @limit");

        parameters.Select(static parameter => parameter.ParameterName)
            .Should().Equal("@status", "@type", "@priority", "@label", "@search", "@limit");
        parameters[0].Value.Should().Be("open");
        parameters[2].Value.Should().Be(1);
        parameters[4].Value.Should().Be("%board%");
        parameters[5].Value.Should().Be(25);
    }

    [Fact]
    public void Non_numeric_priority_is_ignored_like_bd_cli()
    {
        var (sql, parameters) = DoltSql.BuildListQuery(new BeadListQuery { Priority = "high" });

        sql.Should().NotContain("i.priority = @priority");
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Search_pattern_escapes_like_wildcards()
    {
        DoltSql.ToLikePattern("50%_done!").Should().Be("%50!%!_done!!%");
        DoltSql.ToLikePattern(" ui ").Should().Be("%ui%");
    }

    [Fact]
    public void Ready_and_blocked_read_the_views()
    {
        DoltSql.BuildReadyQuery().Sql.Should().Contain("FROM ready_issues i");

        var blocked = DoltSql.BuildBlockedQuery();
        blocked.Sql.Should().Contain("FROM blocked_issues i")
            .And.Contain("i.status NOT IN ('closed', 'pinned')");
        blocked.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Id_lists_are_expanded_into_numbered_parameters()
    {
        var (labelSql, labelParameters) = DoltSql.BuildLabelQuery(["A-1", "A-2"]);
        labelSql.Should().Contain("@l0, @l1");
        labelParameters.Select(static parameter => parameter.ParameterName).Should().Equal("@l0", "@l1");

        var (dependencySql, dependencyParameters) = DoltSql.BuildDependencyQuery(["A-1"]);
        dependencySql.Should().Contain("@d0")
            .And.Contain("LEFT JOIN issues t ON t.id = d.depends_on_issue_id")
            .And.Contain("COALESCE(d.depends_on_issue_id, d.depends_on_external, d.depends_on_wisp_id)");
        dependencyParameters.Should().HaveCount(1);
    }

    [Fact]
    public void Comments_are_filtered_by_issue_and_ordered()
    {
        var (sql, parameters) = DoltSql.BuildCommentQuery("A-1");

        sql.Should().Contain("WHERE c.issue_id = @id").And.Contain("ORDER BY c.created_at, c.id");
        parameters[0].Value.Should().Be("A-1");
    }
}
