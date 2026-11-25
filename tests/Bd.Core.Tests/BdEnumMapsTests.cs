using Bd.Core.Domain;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class BdEnumMapsTests
{
    [Theory]
    [InlineData(BeadStatus.Open, "open")]
    [InlineData(BeadStatus.InProgress, "in_progress")]
    [InlineData(BeadStatus.Blocked, "blocked")]
    [InlineData(BeadStatus.Deferred, "deferred")]
    [InlineData(BeadStatus.Closed, "closed")]
    public void Status_round_trips_through_bd_string(BeadStatus status, string raw)
    {
        BdEnumMaps.ToBdString(status).Should().Be(raw);
        BdEnumMaps.ParseStatus(raw).Should().Be(status);
        BdEnumMaps.ParseStatus(raw.ToUpperInvariant()).Should().Be(status);
    }

    [Theory]
    [InlineData(BeadType.Task, "task")]
    [InlineData(BeadType.Bug, "bug")]
    [InlineData(BeadType.Epic, "epic")]
    [InlineData(BeadType.Chore, "chore")]
    [InlineData(BeadType.Decision, "decision")]
    [InlineData(BeadType.Spike, "spike")]
    public void Type_round_trips_through_bd_string(BeadType type, string raw)
    {
        BdEnumMaps.ToBdString(type).Should().Be(raw);
        BdEnumMaps.ParseType(raw).Should().Be(type);
    }

    [Theory]
    [InlineData(DependencyType.Blocks, "blocks")]
    [InlineData(DependencyType.ParentChild, "parent-child")]
    [InlineData(DependencyType.Related, "related")]
    public void Dependency_round_trips_through_bd_string(DependencyType type, string raw)
    {
        BdEnumMaps.ToBdString(type).Should().Be(raw);
        BdEnumMaps.ParseDependency(raw).Should().Be(type);
    }

    [Fact]
    public void Unknown_values_fall_back_without_throwing()
    {
        BdEnumMaps.ParseStatus("weird-status").Should().Be(BeadStatus.Unknown);
        BdEnumMaps.ParseType("future-type").Should().Be(BeadType.Unknown);
        BdEnumMaps.ParseDependency("future-dep").Should().Be(DependencyType.Unknown);
        BdEnumMaps.ParsePriority("P9").Should().Be(BeadPriority.P4);
    }

    [Theory]
    [InlineData(0, "P0")]
    [InlineData(2, "P2")]
    [InlineData(4, "P4")]
    [InlineData(null, "P2")]
    public void Priority_formats_like_bd(int? value, string expected) =>
        BdEnumMaps.FormatPriority(BdEnumMaps.ToPriority(value)).Should().Be(expected);
}
