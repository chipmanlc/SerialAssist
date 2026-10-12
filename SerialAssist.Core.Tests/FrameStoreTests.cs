using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// FrameStore 规格测试(P6 帧编辑器数据层):帧列表的增/删/改/上移/下移,
/// 边界(首上移/尾下移/越界)安全返回 false 不抛。
/// </summary>
[TestFixture]
public class FrameStoreTests
{
    private static DataFrame F(string name) => new(name, [0x01]);

    private FrameStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new FrameStore();
        _store.Add(F("A"));
        _store.Add(F("B"));
        _store.Add(F("C"));
    }

    // ---------- 核心场景 ----------

    [Test]
    public void Add_追加到尾部_顺序保持()
    {
        Assert.That(_store.Frames.Select(f => f.Name), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void RemoveAt_中间删除_其余前移()
    {
        Assert.That(_store.RemoveAt(1), Is.True);
        Assert.That(_store.Frames.Select(f => f.Name), Is.EqualTo(new[] { "A", "C" }));
    }

    [Test]
    public void MoveUp_中间项_与前一项交换()
    {
        Assert.That(_store.MoveUp(1), Is.True);
        Assert.That(_store.Frames.Select(f => f.Name), Is.EqualTo(new[] { "B", "A", "C" }));
    }

    [Test]
    public void MoveDown_中间项_与后一项交换()
    {
        Assert.That(_store.MoveDown(1), Is.True);
        Assert.That(_store.Frames.Select(f => f.Name), Is.EqualTo(new[] { "A", "C", "B" }));
    }

    [Test]
    public void Replace_原位替换_顺序不变()
    {
        Assert.That(_store.Replace(1, F("X")), Is.True);
        Assert.That(_store.Frames.Select(f => f.Name), Is.EqualTo(new[] { "A", "X", "C" }));
    }

    // ---------- 边界/异常场景 ----------

    [Test]
    public void MoveUp_首项_返回false不移动()
    {
        Assert.That(_store.MoveUp(0), Is.False);
        Assert.That(_store.Frames.Select(f => f.Name), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void MoveDown_末项_返回false不移动()
    {
        Assert.That(_store.MoveDown(2), Is.False);
        Assert.That(_store.Frames.Select(f => f.Name), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [TestCase(-1)]
    [TestCase(3)]
    public void 越界索引_RemoveAt_MoveUp_MoveDown_Replace_返回false不抛(int index)
    {
        Assert.Multiple(() =>
        {
            Assert.That(_store.RemoveAt(index), Is.False);
            Assert.That(_store.MoveUp(index), Is.False);
            Assert.That(_store.MoveDown(index), Is.False);
            Assert.That(_store.Replace(index, F("Z")), Is.False);
        });
        Assert.That(_store.Frames, Has.Count.EqualTo(3), "越界操作不得改动列表");
    }

    [Test]
    public void Add_null帧_抛参数异常()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Add(null!));
    }

    [Test]
    public void 空store_所有操作安全()
    {
        var empty = new FrameStore();
        Assert.Multiple(() =>
        {
            Assert.That(empty.RemoveAt(0), Is.False);
            Assert.That(empty.MoveUp(0), Is.False);
            Assert.That(empty.MoveDown(0), Is.False);
            Assert.That(empty.Frames, Is.Empty);
        });
    }
}

/// <summary>DataFrame.Enabled 规格:默认启用;Enabled 不影响 BuildBytes 产物。</summary>
[TestFixture]
public class DataFrameEnabledTests
{
    [Test]
    public void 默认Enabled为true_旧构造不受影响()
    {
        var frame = new DataFrame("f", [0x01, 0x02]);
        Assert.That(frame.Enabled, Is.True);
    }

    [Test]
    public void Enabled不影响BuildBytes_校验照常附加()
    {
        var frame = new DataFrame("f", [0x01], ChecksumMode.Sum8, delayAfterMs: 5) { Enabled = false };
        Assert.That(frame.BuildBytes(), Is.EqualTo(new byte[] { 0x01, 0x01 }));
    }
}
