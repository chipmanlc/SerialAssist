using System.Text;
using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// HexCodec 规格测试(设计文档 S3):
/// HEX 输入容忍空格/逗号/0x 前缀/大小写;奇数位与非法字符给出行内报错不崩溃;
/// ToHexString 输出大写、两位、空格分隔。
/// </summary>
[TestFixture]
public class HexCodecTests
{
    // ---------- 核心场景 ----------

    [TestCase("01 A2 FF", new byte[] { 0x01, 0xA2, 0xFF })]
    [TestCase("01A2FF", new byte[] { 0x01, 0xA2, 0xFF })]
    [TestCase("01,a2,ff", new byte[] { 0x01, 0xA2, 0xFF })]
    [TestCase("0x01 0xA2 0xFF", new byte[] { 0x01, 0xA2, 0xFF })]
    [TestCase("  01\tA2\nFF  ", new byte[] { 0x01, 0xA2, 0xFF })]
    [TestCase("ff", new byte[] { 0xFF })]
    public void TryParse_合法输入_返回正确字节(string input, byte[] expected)
    {
        var ok = HexCodec.TryParse(input, out var bytes, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(bytes, Is.EqualTo(expected));
            Assert.That(error, Is.Null);
        });
    }

    [Test]
    public void ToHexString_字节序列_输出大写两位空格分隔()
    {
        var text = HexCodec.ToHexString(new byte[] { 0x01, 0xA2, 0xFF });

        Assert.That(text, Is.EqualTo("01 A2 FF"));
    }

    [Test]
    public void ToHexString_TryParse_往返一致()
    {
        var original = new byte[] { 0x00, 0x7F, 0x80, 0xFF };

        var ok = HexCodec.TryParse(HexCodec.ToHexString(original), out var roundTrip, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(roundTrip, Is.EqualTo(original));
        });
    }

    // ---------- 边界场景 ----------

    [Test]
    public void ToHexString_空数组_返回空串()
    {
        Assert.That(HexCodec.ToHexString(Array.Empty<byte>()), Is.EqualTo(""));
    }

    [Test]
    public void TryParse_null_报错不崩溃()
    {
        var ok = HexCodec.TryParse(null, out var bytes, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(bytes, Is.EqualTo(Array.Empty<byte>()));
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void TryParse_纯空白_报错()
    {
        var ok = HexCodec.TryParse("   \t ", out var _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }

    // ---------- 异常场景 ----------

    [Test]
    public void TryParse_奇数位_报错()
    {
        var ok = HexCodec.TryParse("01 A2 F", out var _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain("奇数").Or.Contain("odd").IgnoreCase);
        });
    }

    [TestCase("01 G2 FF")]
    [TestCase("01A2FG")]
    [TestCase("Hello")]
    [TestCase("01 A2 FF Z")]
    public void TryParse_非法字符_报错且不抛异常(string input)
    {
        var ok = HexCodec.TryParse(input, out var _, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, () => $"输入 {input} 不应解析成功");
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }
}
