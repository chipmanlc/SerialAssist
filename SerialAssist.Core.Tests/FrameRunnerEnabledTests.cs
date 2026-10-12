using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// FrameRunner 停用帧语义(P6/S6 启停):连发与循环只发启用帧;
/// 停用帧的 DelayAfterMs 也不得被等待(否则停用一条长延时帧会拖死整轮)。
/// 单发(SendOnceAsync)是用户显式指定,不做启用过滤。
/// </summary>
[TestFixture]
public class FrameRunnerEnabledTests
{
    private sealed class RecordingSender : IAsyncDisposable
    {
        public List<byte[]> Sent { get; } = new();
        public bool Send(byte[] bytes)
        {
            Sent.Add(bytes);
            return true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Test]
    public async Task 连发_跳过停用帧_只发启用帧()
    {
        var sender = new RecordingSender();
        var runner = new FrameRunner(sender.Send);
        var frames = new[]
        {
            new DataFrame("A", [0xAA]),
            new DataFrame("B", [0xBB], delayAfterMs: 10_000) { Enabled = false }, // 若未跳过,光延时就要 10 秒
            new DataFrame("C", [0xCC]),
        };

        var ok = await runner.SendSelectedAsync(frames);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(sender.Sent.Select(b => b[0]), Is.EqualTo(new byte[] { 0xAA, 0xCC }));
        });
    }

    [Test]
    public async Task 全部停用_连发直接完成_不发任何字节()
    {
        var sender = new RecordingSender();
        var runner = new FrameRunner(sender.Send);
        var frames = new[] { new DataFrame("A", [0xAA]) { Enabled = false } };

        var ok = await runner.SendSelectedAsync(frames);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(sender.Sent, Is.Empty);
        });
    }

    [Test]
    public async Task 循环_跳过停用帧_停用帧字节永不出现()
    {
        var sender = new RecordingSender();
        var runner = new FrameRunner(sender.Send);
        var frames = new[]
        {
            new DataFrame("A", [0xAA], delayAfterMs: 5),
            new DataFrame("B", [0xBB]) { Enabled = false },
        };
        using var cts = new CancellationTokenSource(60); // 60ms 后取消,足够跑多轮

        await runner.LoopAsync(frames, cts.Token);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sent, Is.Not.Empty, "循环应至少发出一轮启用帧");
            Assert.That(sender.Sent.All(b => b[0] == 0xAA), Is.True, "停用帧字节不得出现");
        });
    }

    [Test]
    public async Task 单发_显式指定的停用帧_仍然发送()
    {
        var sender = new RecordingSender();
        var runner = new FrameRunner(sender.Send);
        var frame = new DataFrame("A", [0xAA]) { Enabled = false };

        var ok = await runner.SendOnceAsync(frame);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(sender.Sent, Has.Count.EqualTo(1), "单发是显式行为,不做启用过滤");
        });
    }
}
