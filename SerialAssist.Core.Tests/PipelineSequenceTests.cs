using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// ReceivePipeline 序列号规格(P5 压力固证):累计序列号不受背压丢最旧影响,
/// 消费方(视图/日志)用它替代"列表下标"定位新数据——否则追平容量后视图永久假死。
/// 序列号语义:entries[i] 的序列号 = TotalEntries - Count + i。
/// </summary>
[TestFixture]
public class PipelineSequenceTests
{
    private ReceivePipeline _pipeline = null!;

    [SetUp]
    public void SetUp() => _pipeline = new ReceivePipeline();

    [Test]
    public void 未超容量_TotalEntries等于推送数_FirstSequence为0()
    {
        for (var i = 0; i < 100; i++)
        {
            _pipeline.Push(new byte[] { (byte)i });
        }

        Assert.Multiple(() =>
        {
            Assert.That(_pipeline.TotalEntries, Is.EqualTo(100));
            Assert.That(_pipeline.FirstSequence, Is.EqualTo(0));
        });
    }

    [Test]
    public void 超容量丢最旧_TotalEntries继续累计_FirstSequence右移()
    {
        _pipeline.Capacity = 10;
        for (var i = 0; i < 25; i++)
        {
            _pipeline.Push(new byte[] { (byte)i });
        }

        Assert.Multiple(() =>
        {
            Assert.That(_pipeline.TotalEntries, Is.EqualTo(25), "丢弃不计入 TotalEntries 减少");
            Assert.That(_pipeline.FirstSequence, Is.EqualTo(15), "最旧 15 条被背压淘汰");
        });
    }

    [Test]
    public void 背压后_Snapshot首条对应FirstSequence_末条对应TotalEntries减一()
    {
        _pipeline.Capacity = 5;
        for (var i = 0; i < 8; i++)
        {
            _pipeline.Push(new byte[] { (byte)i });
        }

        var entries = _pipeline.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(entries.Count, Is.EqualTo(5));
            Assert.That(entries[0].Bytes[0], Is.EqualTo(3), "最旧保留的是第 3 条(序列号 3)");
            Assert.That(_pipeline.FirstSequence, Is.EqualTo(3));
        });
    }

    [Test]
    public void Clear_TotalEntries归零_消费方据此检测清屏()
    {
        for (var i = 0; i < 20; i++)
        {
            _pipeline.Push(new byte[] { 1 });
        }

        _pipeline.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(_pipeline.TotalEntries, Is.EqualTo(0));
            Assert.That(_pipeline.FirstSequence, Is.EqualTo(0));
        });
    }

    [Test]
    public void 压力场景_持续超容量灌数_序列号推进无空洞()
    {
        // 模拟 115200 洪流:远超容量的持续推送,消费方按序列号应能无缝衔接
        _pipeline.Capacity = 100;
        for (var i = 0; i < 5000; i++)
        {
            _pipeline.Push(new byte[] { (byte)(i & 0xFF) });
        }

        var entries = _pipeline.Snapshot();
        var first = _pipeline.FirstSequence;

        Assert.Multiple(() =>
        {
            Assert.That(_pipeline.TotalEntries, Is.EqualTo(5000));
            // 快照内序列号必须连续:FirstSequence + Count == TotalEntries
            Assert.That(first + entries.Count, Is.EqualTo(_pipeline.TotalEntries));
            // 内容与序列对齐:快照首字节 == 序列号低 8 位
            Assert.That(entries[0].Bytes[0], Is.EqualTo((byte)(first & 0xFF)));
            Assert.That(entries[^1].Bytes[0], Is.EqualTo((byte)((_pipeline.TotalEntries - 1) & 0xFF)));
        });
    }
}
