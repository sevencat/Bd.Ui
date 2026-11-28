using System.Data;
using Bd.Core.Dolt;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>用 DataTableReader（实现 IDataRecord）验证 issues/comments 行到领域模型的映射。</summary>
public sealed class DoltStoreMappingTests
{
    [Fact]
    public void Bead_row_maps_all_columns()
    {
        using var table = CreateBeadTable();
        table.Rows.Add(
            "FHeroes2Sharp-a7l", "移植 castles", "描述", "in_progress", 1, "feature",
            "owner@example.com", "nwms", new DateTime(2026, 11, 20, 8, 0, 0), new DateTime(2026, 11, 21, 9, 30, 0),
            "FHeroes2Sharp-3so");

        var bead = ReadSingle(table);

        bead.Id.Should().Be("FHeroes2Sharp-a7l");
        bead.Title.Should().Be("移植 castles");
        bead.Description.Should().Be("描述");
        bead.StatusRaw.Should().Be("in_progress");
        bead.PriorityValue.Should().Be(1);
        bead.TypeRaw.Should().Be("feature");
        bead.Owner.Should().Be("owner@example.com");
        bead.CreatedBy.Should().Be("nwms");
        bead.CreatedAt.Should().Be(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));
        bead.CreatedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        bead.UpdatedAt.Should().Be(new DateTimeOffset(2026, 11, 21, 9, 30, 0, TimeSpan.Zero));
        bead.ParentId.Should().Be("FHeroes2Sharp-3so");
    }

    [Fact]
    public void Missing_optional_columns_fall_back_to_defaults()
    {
        using var table = CreateBeadTable();
        table.Rows.Add("Bd_Ui-1", "标题", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
            DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);

        var bead = ReadSingle(table);

        bead.Description.Should().BeNull();
        bead.StatusRaw.Should().Be("open");
        bead.PriorityValue.Should().Be(2);
        bead.TypeRaw.Should().Be("task");
        bead.CreatedAt.Should().BeNull();
        bead.ParentId.Should().BeNull();
        bead.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Dependency_row_uses_coalesced_target()
    {
        using var table = new DataTable();
        table.Columns.Add("issue_id", typeof(string));
        table.Columns.Add("target_id", typeof(string));
        table.Columns.Add("dependency_type", typeof(string));
        table.Columns.Add("target_title", typeof(string));
        table.Columns.Add("target_status", typeof(string));
        table.Columns.Add("target_type", typeof(string));
        table.Rows.Add("A-1", "A-2", "blocks", "目标任务", "open", "task");
        table.Rows.Add("A-1", "external:JIRA-9", "related", DBNull.Value, DBNull.Value, DBNull.Value);

        using var reader = new DataTableReader(table);
        var links = new List<Bd.Core.Domain.DependencyLink>();
        while (reader.Read())
        {
            links.Add(DoltStore.MapDependency(reader));
        }

        links.Should().HaveCount(2);
        links[0].TargetId.Should().Be("A-2");
        links[0].Title.Should().Be("目标任务");
        links[0].DependencyTypeRaw.Should().Be("blocks");
        links[1].TargetId.Should().Be("external:JIRA-9");
        links[1].Title.Should().BeNull();
    }

    [Fact]
    public void Comment_row_maps_author_and_timestamp()
    {
        using var table = new DataTable();
        table.Columns.Add("id", typeof(string));
        table.Columns.Add("issue_id", typeof(string));
        table.Columns.Add("author", typeof(string));
        table.Columns.Add("text", typeof(string));
        table.Columns.Add("created_at", typeof(DateTime));
        table.Rows.Add("5", "A-1", "nwms", "已修复", new DateTime(2026, 11, 26, 10, 0, 0));

        using var reader = new DataTableReader(table);
        reader.Read();
        var comment = DoltStore.MapComment(reader);

        comment.Id.Should().Be("5");
        comment.IssueId.Should().Be("A-1");
        comment.Author.Should().Be("nwms");
        comment.Text.Should().Be("已修复");
        comment.CreatedAt.Should().Be(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero));
    }

    private static Bd.Core.Domain.Bead ReadSingle(DataTable table)
    {
        using var reader = new DataTableReader(table);
        reader.Read();
        return DoltStore.MapBead(reader);
    }

    private static DataTable CreateBeadTable()
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(string));
        table.Columns.Add("title", typeof(string));
        table.Columns.Add("description", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("priority", typeof(int));
        table.Columns.Add("issue_type", typeof(string));
        table.Columns.Add("owner", typeof(string));
        table.Columns.Add("created_by", typeof(string));
        table.Columns.Add("created_at", typeof(DateTime));
        table.Columns.Add("updated_at", typeof(DateTime));
        table.Columns.Add("parent_id", typeof(string));
        return table;
    }
}
