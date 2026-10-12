using System.Text;
using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// SendPreview 规格测试:发送框双视图实时预览。
/// 文本模式 → 预览显示将发送字节的 HEX(含行尾);HEX 模式 → 预览显示解码文本(控制字符转义)。
/// 空输入(打字中)预览为空不报错;非法 HEX 显示错误提示。
/// </summary>
[TestFixture]
public class SendPreviewTests
{
    // ---------- 核心场景 ----------

    [Test]
    public void 文本模式_预览显示将发送字节的HEX_含行尾()
    {
        var preview = SendPreview.Render(SendMode.Text, "admin", Encoding.ASCII, NewlineMode.CR);

        Assert.That(preview, Is.EqualTo("61 64 6D 69 6E 0D"));
    }

    [Test]
    public void 文本模式_中文GBK编码_按GBK字节预览()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var preview = SendPreview.Render(SendMode.Text, "中", Encoding.GetEncoding("GBK"), NewlineMode.None);

        Assert.That(preview, Is.EqualTo("D6 D0"));
    }

    [Test]
    public void HEX模式_预览显示解码文本_不附加行尾()
    {
        // 行尾只作用于文本模式:HEX 模式预览必须与实际上线字节逐字节一致(无 0D 掺入)
        var preview = SendPreview.Render(SendMode.Hex, "61 64 6D 69 6E", Encoding.ASCII, NewlineMode.CR);

        Assert.That(preview, Is.EqualTo("admin"));
    }

    [Test]
    public void HEX模式_任意行尾参数_预览不出现转义行尾()
    {
        var preview = SendPreview.Render(SendMode.Hex, "61", Encoding.ASCII, NewlineMode.CRLF);

        Assert.That(preview, Is.EqualTo("a"));
    }

    [Test]
    public void HEX模式_GBK解码_中文正常显示()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var preview = SendPreview.Render(SendMode.Hex, "D6 D0", Encoding.GetEncoding("GBK"), NewlineMode.None);

        Assert.That(preview, Is.EqualTo("中"));
    }

    // ---------- 不变量:预览与实际发送字节一致 ----------

    [Test]
    public void 不变量_文本模式预览的HEX_解析回字节等于实际发送载荷()
    {
        var preview = SendPreview.Render(SendMode.Text, "login", Encoding.UTF8, NewlineMode.CRLF);

        var ok = HexCodec.TryParse(preview, out var parsed, out _);
        var actual = SendFormatter.Format(SendMode.Text, "login", Encoding.UTF8, NewlineMode.CRLF);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(parsed, Is.EqualTo(actual.Bytes));
        });
    }

    // ---------- 边界/异常场景 ----------

    [TestCase(SendMode.Text)]
    [TestCase(SendMode.Hex)]
    public void 空输入_预览为空串_不显示错误(SendMode mode)
    {
        // 打字过程中的空态是常态,不该闪错误提示
        Assert.That(SendPreview.Render(mode, "   ", Encoding.ASCII, NewlineMode.CR), Is.Empty);
    }

    [Test]
    public void HEX模式_非法输入_显示错误提示()
    {
        var preview = SendPreview.Render(SendMode.Hex, "ZZ", Encoding.ASCII, NewlineMode.CR);

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.Not.Empty);
            Assert.That(preview, Does.Contain("非法"));
        });
    }
}
