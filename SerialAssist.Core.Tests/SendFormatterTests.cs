using System.Text;
using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// SendFormatter 规格测试(S3 发送侧,2026-10-10 用户修订):
/// 文本/HEX 双模式;行尾可选 无/CR/LF/CRLF(用户设备只认 \r,默认 CR);
/// HEX 复用 HexCodec 容忍规则;空输入报错不发。
/// </summary>
[TestFixture]
public class SendFormatterTests
{
    // ---------- 核心场景 ----------

    [Test]
    public void Hex模式_带空格输入_转字节()
    {
        var result = SendFormatter.Format(SendMode.Hex, "01 A2 FF", newline: NewlineMode.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Bytes, Is.EqualTo(new byte[] { 0x01, 0xA2, 0xFF }));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public void 文本模式_ASCII输入_按编码转字节()
    {
        var result = SendFormatter.Format(SendMode.Text, "Hi", newline: NewlineMode.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Bytes, Is.EqualTo(new byte[] { 0x48, 0x69 }));
        });
    }

    [Test]
    public void 文本模式_自定义编码_按编码解码()
    {
        var result = SendFormatter.Format(SendMode.Text, "中文", Encoding.UTF8, NewlineMode.None);

        Assert.That(result.Bytes, Is.EqualTo("中文"u8.ToArray()));
    }

    [TestCase(NewlineMode.None)]
    [TestCase(NewlineMode.CR)]
    [TestCase(NewlineMode.LF)]
    [TestCase(NewlineMode.CRLF)]
    public void Hex模式_不附加行尾_协议帧字节必须精确(NewlineMode ending)
    {
        // HEX 是字节级精确构造场景:行尾参数对 HEX 模式完全无效(0x0D 混入二进制帧是事故)
        var result = SendFormatter.Format(SendMode.Hex, "01", newline: ending);

        Assert.That(result.Bytes, Is.EqualTo(new byte[] { 0x01 }));
    }

    [Test]
    public void 文本模式_默认行尾CR_用户设备只认回车()
    {
        var result = SendFormatter.Format(SendMode.Text, "admin", newline: NewlineMode.CR);

        Assert.That(result.Bytes, Is.EqualTo("admin\r"u8.ToArray()));
    }

    // ---------- 异常场景 ----------

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void 空输入_报错不发(string? input)
    {
        var result = SendFormatter.Format(SendMode.Text, input, newline: NewlineMode.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Bytes, Is.EqualTo(Array.Empty<byte>()));
        });
    }

    [Test]
    public void Hex模式_非法输入_透传HexCodec报错()
    {
        var result = SendFormatter.Format(SendMode.Hex, "01 Z2", newline: NewlineMode.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        });
    }
}
