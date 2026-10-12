using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// NewlineOptions 规格测试:下拉框显示顺序 ↔ NewlineMode 数值的映射契约。
/// 2026-10-12 真机事故固证:UI 曾用 (NewlineMode)SelectedIndex 直接强转,
/// 而下拉框顺序是 [CR, LF, CRLF, 无]、枚举却是 None=0/CR=1/LF=2/CRLF=3,
/// 导致界面选 CR 实际发 None(admin 登入只发 5 字节)。映射收进 Core,测试钉死。
/// </summary>
[TestFixture]
public class NewlineOptionsTests
{
    // ---------- 核心场景 ----------

    [TestCase(0, NewlineMode.CR)]
    [TestCase(1, NewlineMode.LF)]
    [TestCase(2, NewlineMode.CRLF)]
    [TestCase(3, NewlineMode.None)]
    public void FromIndex_下拉框四位_一一对应正确枚举(int index, NewlineMode expected)
    {
        Assert.That(NewlineOptions.FromIndex(index), Is.EqualTo(expected));
    }

    [Test]
    public void DisplayNames_四项_与FromIndex索引对齐()
    {
        Assert.That(NewlineOptions.DisplayNames, Has.Length.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(NewlineOptions.DisplayNames[0], Does.Contain("CR"));
            Assert.That(NewlineOptions.DisplayNames[3], Is.EqualTo("无"));
        });
    }

    // ---------- 异常场景 ----------

    [TestCase(-1)]
    [TestCase(4)]
    [TestCase(99)]
    public void FromIndex_越界索引_回退默认CR(int index)
    {
        // 越界只可能是程序 bug,退到文档化默认值 CR(DropDownList 正常不会出现 -1)
        Assert.That(NewlineOptions.FromIndex(index), Is.EqualTo(SendFormatter.DefaultNewline));
    }
}
