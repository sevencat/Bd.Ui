using Bd.Core.BdCli;
using Bd.Core.Domain;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class BdJsonTests
{
    private const string ListEnvelope = """
    {
      "data": [
        {
          "id": "Bd_Ui-x1",
          "title": "看板视图",
          "description": "五列布局",
          "status": "in_progress",
          "priority": 1,
          "issue_type": "task",
          "owner": "alice",
          "created_by": "sevencat",
          "created_at": "2026-11-25T01:37:52Z",
          "updated_at": "2026-11-25T02:00:00Z",
          "labels": ["ui", "archived"],
          "dependencies": [
            { "id": "Bd_Ui-x0", "title": "父任务", "status": "closed", "dependency_type": "blocks" }
          ],
          "parent": "Bd_Ui-epic",
          "dependency_count": 1,
          "dependent_count": 2,
          "comment_count": 3
        }
      ],
      "schema_version": 1
    }
    """;

    [Fact]
    public void Parses_list_envelope_and_derived_flags()
    {
        var beads = BdJson.ParseArray<Bead>(ListEnvelope, out var schema);

        schema.Should().Be(1);
        var bead = beads.Should().ContainSingle().Subject;
        bead.Id.Should().Be("Bd_Ui-x1");
        bead.Status.Should().Be(BeadStatus.InProgress);
        bead.Priority.Should().Be(BeadPriority.P1);
        bead.Type.Should().Be(BeadType.Task);
        bead.IsArchived.Should().BeTrue();
        bead.ParentId.Should().Be("Bd_Ui-epic");
        bead.CommentCount.Should().Be(3);
        bead.Dependencies.Should().ContainSingle().Which.IsOpenBlock.Should().BeFalse();
    }

    [Fact]
    public void Parses_single_object_payload()
    {
        const string show = """{"data":{"id":"Bd_Ui-x2","title":"详情","status":"open"},"schema_version":1}""";

        var bead = BdJson.ParseSingleOrDefault<Bead>(show, out var schema);

        schema.Should().Be(1);
        bead.Should().NotBeNull();
        bead!.Id.Should().Be("Bd_Ui-x2");
        bead.Status.Should().Be(BeadStatus.Open);
    }

    [Fact]
    public void Parses_error_payload()
    {
        const string error = """
        {"data":{"error":"no issues found matching the provided IDs","hint":"try bd history"},"schema_version":1}
        """;

        BdJson.TryReadError(error, out var payload).Should().BeTrue();
        payload!.Error.Should().Contain("no issues found");
        payload.Hint.Should().Contain("history");
    }

    [Fact]
    public void Missing_optional_fields_are_tolerated()
    {
        const string minimal = """{"data":[{"id":"bd-1"}],"schema_version":1}""";

        var bead = BdJson.ParseArray<Bead>(minimal, out _).Single();

        bead.Title.Should().BeEmpty();
        bead.Labels.Should().BeEmpty();
        bead.Status.Should().Be(BeadStatus.Open);
        bead.Priority.Should().Be(BeadPriority.P2);
    }

    [Fact]
    public void Malformed_json_throws_bd_exception()
    {
        var act = () => BdJson.ParseArray<Bead>("not json", out _);
        act.Should().Throw<BdException>().Which.Code.Should().Be(BdErrorCode.Unknown);
    }

    [Fact]
    public void Empty_payload_returns_empty_list()
    {
        BdJson.ParseArray<Bead>("""{"data":[],"schema_version":1}""", out _).Should().BeEmpty();
    }
}
