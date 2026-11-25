using Bd.Core.BdCli;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>写串行化压力测试：embedded 数据库只允许单写者，这里验证互斥、顺序与取消语义。</summary>
public sealed class BdWriteGateTests
{
    private const int Writers = 20;
    private const int Rounds = 100;

    [Fact]
    public async Task 二十个并发写严格串行且顺序可重放()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var gate = new BdWriteGate();
            var state = new GateProbe();

            var tasks = Enumerable.Range(0, Writers).Select(index => gate.RunAsync(
                async () =>
                {
                    state.Enter(index);
                    await Task.Yield();
                    state.Exit(index);
                    return index;
                },
                CancellationToken.None));

            var results = await Task.WhenAll(tasks);

            results.Should().BeEquivalentTo(Enumerable.Range(0, Writers));
            state.MaxConcurrency.Should().Be(1, $"第 {round} 轮不应出现并发写");
            state.EntryOrder.Should().HaveCount(Writers).And.OnlyHaveUniqueItems();
            state.EntryOrder.Should().Equal(state.ExitOrder, "进入与离开顺序应一致，说明临界区没有交错");
            gate.ActiveWrites.Should().Be(0);
            gate.IsIdle.Should().BeTrue();
        }
    }

    [Fact]
    public async Task 取消等待不泄漏信号量也不阻塞后续写()
    {
        var gate = new BdWriteGate();
        using var holderStarted = new ManualResetEventSlim();
        var releaseHolder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var holder = gate.RunAsync(
            async () =>
            {
                holderStarted.Set();
                await releaseHolder.Task.ConfigureAwait(false);
            },
            CancellationToken.None);

        holderStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        gate.ActiveWrites.Should().Be(1);

        using var cts = new CancellationTokenSource();
        var waiters = Enumerable.Range(0, 10)
            .Select(_ => gate.RunAsync(() => Task.CompletedTask, cts.Token))
            .ToArray();

        cts.Cancel();

        foreach (var waiter in waiters)
        {
            var act = async () => await waiter;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        gate.ActiveWrites.Should().Be(1, "等待者被取消不应动到临界区计数");

        releaseHolder.SetResult();
        await holder;

        gate.ActiveWrites.Should().Be(0);
        gate.IsIdle.Should().BeTrue();

        (await gate.RunAsync(() => Task.FromResult("ok"), CancellationToken.None)).Should().Be("ok");
    }

    [Fact]
    public async Task 已取消的令牌不会进入临界区()
    {
        var gate = new BdWriteGate();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await gate.RunAsync(() => Task.FromResult(1), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        gate.ActiveWrites.Should().Be(0);
        gate.IsIdle.Should().BeTrue();
    }

    [Fact]
    public async Task 写操作抛异常后锁仍会被释放()
    {
        var gate = new BdWriteGate();

        var act = async () => await gate.RunAsync<int>(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("写失败");
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        gate.ActiveWrites.Should().Be(0);

        (await gate.RunAsync(() => Task.FromResult(42), CancellationToken.None)).Should().Be(42);
    }

    private sealed class GateProbe
    {
        private readonly object _sync = new();
        private readonly List<int> _entryOrder = [];
        private readonly List<int> _exitOrder = [];
        private int _active;
        private int _maxConcurrency;

        public IReadOnlyList<int> EntryOrder
        {
            get
            {
                lock (_sync)
                {
                    return [.. _entryOrder];
                }
            }
        }

        public IReadOnlyList<int> ExitOrder
        {
            get
            {
                lock (_sync)
                {
                    return [.. _exitOrder];
                }
            }
        }

        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

        public void Enter(int index)
        {
            var active = Interlocked.Increment(ref _active);
            if (active > Volatile.Read(ref _maxConcurrency))
            {
                Volatile.Write(ref _maxConcurrency, active);
            }

            lock (_sync)
            {
                _entryOrder.Add(index);
            }
        }

        public void Exit(int index)
        {
            lock (_sync)
            {
                _exitOrder.Add(index);
            }

            Interlocked.Decrement(ref _active);
        }
    }
}
