using System.Text;
using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// ReceivePipeline 规格测试(设计文档 S4/S5/S9 关键点1/2/4):
/// 模型层无条件追加(冻结与否是视图层的事);容量背压丢最旧+丢弃计数;
/// 每条目带时间戳;渲染器是同一字节源的两种投影。
/// </summary>
[TestFixture]
public class ReceivePipelineTests
{
    private ReceivePipeline _pipeline = null!;

    [SetUp]
    public void SetUp() => _pipeline = new ReceivePipeline();

    // ---------- 核心场景 ----------

    [Test]
    public void Push_单块_条目含原始字节与时间戳()
    {
        var ts = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        _pipeline.Push(new byte[] { 0xAA }, ts);

        var entries = _pipeline.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Bytes, Is.EqualTo(new byte[] { 0xAA }));
            Assert.That(entries[0].Timestamp, Is.EqualTo(ts));
        });
    }

    [Test]
    public void Push_多块_保持顺序()
    {
        _pipeline.Push(new byte[] { 0x01 });
        _pipeline.Push(new byte[] { 0x02 });
        _pipeline.Push(new byte[] { 0x03 });

        var entries = _pipeline.Snapshot();
        Assert.That(entries.Select(e => e.Bytes[0]), Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
    }

    [Test]
    public void Push_不传时间戳_自动取当前时间()
    {
        var before = DateTimeOffset.Now.AddSeconds(-5);

        _pipeline.Push(new byte[] { 0x01 });

        Assert.That(_pipeline.Snapshot()[0].Timestamp, Is.GreaterThan(before));
    }

    [Test]
    public void Push_触发Updated事件()
    {
        var fired = 0;
        _pipeline.Updated += () => fired++;

        _pipeline.Push(new byte[] { 0x01 });
        _pipeline.Push(new byte[] { 0x02 });

        Assert.That(fired, Is.EqualTo(2));
    }

    [Test]
    public void Clear_清空条目与丢弃计数()
    {
        _pipeline.Push(new byte[] { 0x01 });

        _pipeline.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(_pipeline.Snapshot(), Is.Empty);
            Assert.That(_pipeline.DroppedByteCount, Is.EqualTo(0));
        });
    }

    // ---------- 边界场景 ----------

    [Test]
    public void Push_空块_不产生条目不触发事件()
    {
        var fired = 0;
        _pipeline.Updated += () => fired++;

        _pipeline.Push(Array.Empty<byte>());

        Assert.Multiple(() =>
        {
            Assert.That(_pipeline.Snapshot(), Is.Empty);
            Assert.That(fired, Is.EqualTo(0));
        });
    }

    [Test]
    public void Push_恰好等于容量_不丢弃()
    {
        _pipeline.Capacity = 3;
        _pipeline.Push(new byte[] { 0x01 });
        _pipeline.Push(new byte[] { 0x02 });
        _pipeline.Push(new byte[] { 0x03 });

        Assert.Multiple(() =>
        {
            Assert.That(_pipeline.Snapshot(), Has.Count.EqualTo(3));
            Assert.That(_pipeline.DroppedByteCount, Is.EqualTo(0));
        });
    }

    // ---------- 异常场景 ----------

    [Test]
    public void Push_超容量_丢最旧且丢弃计数累加字节()
    {
        _pipeline.Capacity = 2;
        _pipeline.Push(new byte[] { 0x01, 0x02 });
        _pipeline.Push(new byte[] { 0x03 });
        _pipeline.Push(new byte[] { 0x04, 0x05, 0x06 });

        var entries = _pipeline.Snapshot();
        Assert.Multiple(() =>
        {
            // 丢掉最早的 0x01,0x02
            Assert.That(entries.Select(e => e.Bytes[0]), Is.EqualTo(new byte[] { 0x03, 0x04 }));
            Assert.That(_pipeline.DroppedByteCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Push_null_报参数异常()
    {
        Assert.Throws<ArgumentNullException>(() => _pipeline.Push(null!));
    }
}

/// <summary>渲染器规格:同一字节源,文本/HEX 两种投影(设计文档 S5)。</summary>
[TestFixture]
public class RendererTests
{
    [Test]
    public void HexRenderer_输出大写两位空格分隔()
    {
        var text = new HexRenderer().Render(new byte[] { 0x01, 0xA2, 0xFF });

        Assert.That(text, Is.EqualTo("01 A2 FF"));
    }

    [Test]
    public void TextRenderer_可打印字符_原样输出()
    {
        var text = new TextRenderer().Render("Hi!\r\n"u8.ToArray());

        Assert.That(text, Is.EqualTo("Hi!\r\n"));
    }

    [Test]
    public void TextRenderer_控制字符_显示为点号()
    {
        // 0x00 和 0x01 不可打印;Tab(0x09) 保留
        var text = new TextRenderer().Render(new byte[] { (byte)'A', 0x00, 0x01, (byte)'B', 0x09, (byte)'C' });

        Assert.That(text, Is.EqualTo("A..B\tC"));
    }

    [Test]
    public void TextRenderer_自定义编码_按编码解码()
    {
        var renderer = new TextRenderer(Encoding.UTF8);

        var text = renderer.Render("中文"u8.ToArray());

        Assert.That(text, Is.EqualTo("中文"));
    }

    [Test]
    public void TextRenderer_GBK解码_中文工位设备回显可读()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding("GBK");
        var renderer = new TextRenderer(gbk);

        var text = renderer.Render(gbk.GetBytes("登录成功"));

        Assert.That(text, Is.EqualTo("登录成功"));
    }

    [Test]
    public void 两种渲染器_同一字节源_互不影响()
    {
        var bytes = new byte[] { 0x48, 0x69 }; // "Hi"
        var hex = new HexRenderer();
        var text = new TextRenderer();

        Assert.Multiple(() =>
        {
            Assert.That(hex.Render(bytes), Is.EqualTo("48 69"));
            Assert.That(text.Render(bytes), Is.EqualTo("Hi"));
            // 再渲染一遍结果一致(渲染器无状态)
            Assert.That(hex.Render(bytes), Is.EqualTo("48 69"));
        });
    }
}
