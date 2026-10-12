using System.Text;
using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// TextEscapeCodec 规格测试:数据帧文本输入,C 风格转义 → 字节。
/// 转义集:\r \n \t \0 \a \b \f \v \\ \xHH(1~2 位十六进制);
/// 其余字符按注入编码(GBK/UTF-8)编码;非法转义报 error 不抛。
/// </summary>
[TestFixture]
public class TextEscapeCodecTests
{
    // ---------- 核心场景 ----------

    [Test]
    public void 文本加回车转义_追加0D()
    {
        var ok = TextEscapeCodec.TryParse("admin\\r", Encoding.ASCII, out var bytes, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(bytes, Is.EqualTo("admin\r"u8.ToArray()));
        });
    }

    [Test]
    public void 常见转义全集合_逐一映射()
    {
        var ok = TextEscapeCodec.TryParse("\\r\\n\\t\\0\\a\\b\\f\\v\\\\", Encoding.ASCII, out var bytes, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(bytes, Is.EqualTo(new byte[] { 0x0D, 0x0A, 0x09, 0x00, 0x07, 0x08, 0x0C, 0x0B, 0x5C }));
        });
    }

    [Test]
    public void xHH转义_一到两位十六进制()
    {
        var ok = TextEscapeCodec.TryParse("\\x41\\xB", Encoding.ASCII, out var bytes, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(bytes, Is.EqualTo(new byte[] { 0x41, 0x0B }));
        });
    }

    [Test]
    public void 转义与普通字符混排_按序拼接()
    {
        var ok = TextEscapeCodec.TryParse("AT\\r\\n", Encoding.ASCII, out var bytes, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(bytes, Is.EqualTo(new byte[] { 0x41, 0x54, 0x0D, 0x0A }));
        });
    }

    [Test]
    public void 中文按GBK编码_多字节正确()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var ok = TextEscapeCodec.TryParse("中", Encoding.GetEncoding("GBK"), out var bytes, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(bytes, Is.EqualTo(new byte[] { 0xD6, 0xD0 }));
        });
    }

    // ---------- 异常场景 ----------

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void 空输入_报错不发(string? input)
    {
        var ok = TextEscapeCodec.TryParse(input, Encoding.ASCII, out var bytes, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(bytes, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        });
    }

    [Test]
    public void 未知转义_报错指出序列()
    {
        var ok = TextEscapeCodec.TryParse("abc\\q", Encoding.ASCII, out var bytes, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(bytes, Is.Empty);
            Assert.That(error, Does.Contain("\\q"));
        });
    }

    [Test]
    public void 末尾孤立反斜杠_报错()
    {
        var ok = TextEscapeCodec.TryParse("abc\\", Encoding.ASCII, out var bytes, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(bytes, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        });
    }

    [TestCase("\\x")]
    [TestCase("\\xGG")]
    [TestCase("\\x123")] // 超过 2 位有歧义,拒绝而不是静默截断
    public void x转义非法_报错不发(string input)
    {
        var ok = TextEscapeCodec.TryParse(input, Encoding.ASCII, out var bytes, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(bytes, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        });
    }
}
