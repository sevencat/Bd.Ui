using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;
using Bd.Core.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class BdCliStoreTests
{
    private const string ListEnvelope = """
    {"data":[{"id":"bd-1","title":"任务一","status":"open","priority":1,"issue_type":"task","labels":["ui"]}],"schema_version":1}
    """;

    [Fact]
    public async Task List_passes_json_and_unlimited_flags()
    {
        var runner = new FakeBdProcessRunner().MapJson("list", ListEnvelope);
        var store = new BdCliStore(runner, "C:\\repo", "tester");

        var beads = await store.ListAsync(new BeadListQuery { Status = "open" }, CancellationToken.None);

        beads.Should().ContainSingle().Which.Id.Should().Be("bd-1");
        var request = runner.Requests.Single();
        request.Arguments.Should().Contain(["--json", "-n", "0", "-s", "open"]);
        request.WorkingDirectory.Should().Be("C:\\repo");
        request.Actor.Should().Be("tester");
    }

    [Fact]
    public async Task Create_returns_id_from_silent_output()
    {
        var runner = new FakeBdProcessRunner().Map("create", _ => new BdProcessResult { ExitCode = 0, StdOut = "bd-42\n" });
        var store = new BdCliStore(runner, "C:\\repo", "tester");

        var id = await store.CreateAsync(
            new CreateBeadRequest
            {
                Title = "新任务",
                Type = "epic",
                Priority = 1,
                ParentId = "bd-parent",
                Labels = ["core"]
            },
            CancellationToken.None);

        id.Should().Be("bd-42");
        var arguments = runner.Requests.Single().Arguments;
        arguments.Should().Contain("--parent").And.Contain("bd-parent");
        arguments.Should().Contain("--silent");
        arguments.Should().Contain("-l");
    }

    [Fact]
    public async Task Missing_issue_maps_to_not_found()
    {
        const string errorJson = """
        {"data":{"error":"no issues found matching the provided IDs"},"schema_version":1}
        """;
        var runner = new FakeBdProcessRunner().Map("show", _ => new BdProcessResult
        {
            ExitCode = 1,
            StdOut = errorJson,
            StdErr = "Issue bd-x not found"
        });
        var store = new BdCliStore(runner, "C:\\repo");

        var act = () => store.GetAsync("bd-x", CancellationToken.None);

        (await act.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.NotFound);
    }

    [Fact]
    public async Task Locked_database_maps_to_lock_busy()
    {
        var runner = new FakeBdProcessRunner().MapFailure("update", "Error: database is locked");
        var store = new BdCliStore(runner, "C:\\repo");

        var act = () => store.UpdateAsync("bd-1", new UpdateBeadRequest { Status = "in_progress" }, CancellationToken.None);

        (await act.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.LockBusy);
    }

    [Fact]
    public async Task Comments_parse_tolerates_alternate_field_names()
    {
        const string commentsJson = """
        {"data":[{"id":"c1","body":"第一条","created_by":"alice","created_at":"2026-11-25T01:00:00Z"},
                 {"id":"c2","text":"第二条","author":"bob","created_at":"2026-11-25T02:00:00Z"}],"schema_version":1}
        """;
        var runner = new FakeBdProcessRunner().MapJson("comments", commentsJson);
        var store = new BdCliStore(runner, "C:\\repo");

        var comments = await store.ListCommentsAsync("bd-1", CancellationToken.None);

        comments.Should().HaveCount(2);
        comments[0].Text.Should().Be("第一条");
        comments[0].Author.Should().Be("alice");
        comments[1].Text.Should().Be("第二条");
    }

    [Fact]
    public async Task Write_commands_run_through_gate_serially()
    {
        var runner = new FakeBdProcessRunner
        {
            Delay = TimeSpan.FromMilliseconds(10),
            Fallback = _ => new BdProcessResult { ExitCode = 0, StdOut = "bd-ok" }
        };
        var gate = new BdWriteGate();
        var store = new BdCliStore(runner, "C:\\repo", gate: gate);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => store.AddCommentAsync("bd-1", $"c{index}", CancellationToken.None)));

        runner.CallCount.Should().Be(8);
        runner.MaxConcurrency.Should().Be(1);
        gate.ActiveWrites.Should().Be(0);
    }

    [Fact]
    public async Task Version_is_extracted_from_text_output()
    {
        var runner = new FakeBdProcessRunner().Map("version", _ => new BdProcessResult
        {
            ExitCode = 0,
            StdOut = "bd version 1.3.4 (7ef2fd05e: HEAD@7ef2fd05eb0c)"
        });
        var store = new BdCliStore(runner, "C:\\repo");

        (await store.GetVersionAsync(CancellationToken.None)).Should().Be("1.3.4");
    }

    [Fact]
    public async Task Update_without_changes_skips_process_call()
    {
        var runner = new FakeBdProcessRunner();
        var store = new BdCliStore(runner, "C:\\repo");

        await store.UpdateAsync("bd-1", new UpdateBeadRequest(), CancellationToken.None);

        runner.CallCount.Should().Be(0);
    }
}
