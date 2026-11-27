using Bd.Core.Dolt;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>写路径里不依赖数据库的纯函数：ID 后缀、事件 JSON、时间格式。</summary>
public sealed class DoltWriteHelpersTests
{
    [Fact]
    public void IdSuffix_is_three_lowercase_base36_chars()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var suffix = DoltStore.BuildIdSuffix(new Random(seed));

            suffix.Should().HaveLength(3);
            suffix.Should().MatchRegex("^[0-9a-z]{3}$");
        }
    }

    [Fact]
    public void IdSuffix_varies_with_random_state()
    {
        var suffixes = Enumerable.Range(0, 20)
            .Select(seed => DoltStore.BuildIdSuffix(new Random(seed)))
            .ToArray();

        suffixes.Distinct(StringComparer.Ordinal).Should().HaveCountGreaterThan(10);
    }

    [Fact]
    public void Delta_json_sorts_keys_and_keeps_value_types()
    {
        var json = DoltStore.BuildDeltaJson(
        [
            new("status", "in_progress"),
            new("priority", 1),
            new("assignee", null)
        ]);

        json.Should().Be("""{"assignee":null,"priority":1,"status":"in_progress"}""");
    }

    [Fact]
    public void Snapshot_json_skips_empty_optionals_and_uses_utc_timestamps()
    {
        var snapshot = new IssueSnapshot(
            "Bd_Ui-abc",
            "标题",
            Description: string.Empty,
            Design: string.Empty,
            AcceptanceCriteria: string.Empty,
            Notes: "备注",
            Status: "open",
            Priority: 2,
            IssueType: "task",
            Assignee: null,
            Owner: null,
            CreatedAt: new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc),
            CreatedBy: "sevencat",
            UpdatedAt: new DateTime(2026, 11, 27, 9, 30, 5, DateTimeKind.Utc),
            StartedAt: null,
            ClosedAt: null,
            ClosedBySession: null,
            CloseReason: null,
            RowLock: 42);

        var json = DoltStore.BuildSnapshotJson(snapshot);

        json.Should().Contain("\"title\":\"标题\"");
        json.Should().Contain("\"notes\":\"备注\"");
        json.Should().Contain("\"created_at\":\"2026-11-27T08:00:00Z\"");
        json.Should().Contain("\"updated_at\":\"2026-11-27T09:30:05Z\"");
        json.Should().NotContain("\"description\"");
        json.Should().NotContain("\"assignee\"");
        json.Should().NotContain("\"closed_at\"");
    }

    [Fact]
    public void Format_timestamp_uses_second_precision()
    {
        DoltStore.FormatTimestamp(new DateTime(2026, 11, 27, 8, 30, 5, 999, DateTimeKind.Utc))
            .Should().Be("2026-11-27T08:30:05Z");
    }
}
