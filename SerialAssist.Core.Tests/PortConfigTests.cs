using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// PortConfig 规格测试(设计文档 S2):
/// 波特率/数据位/停止位/校验/流控全可配;非法值经 Validate 报错不崩溃;
/// 可 JSON 序列化持久化;值相等即可判"参数未变,无需重启会话"。
/// </summary>
[TestFixture]
public class PortConfigTests
{
    // ---------- 核心场景 ----------

    [Test]
    public void 默认配置_9600_8_None_None_None()
    {
        var config = new PortConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.BaudRate, Is.EqualTo(9600));
            Assert.That(config.DataBits, Is.EqualTo(8));
            Assert.That(config.StopBits, Is.EqualTo(StopBitsMode.One));
            Assert.That(config.Parity, Is.EqualTo(ParityMode.None));
            Assert.That(config.Handshake, Is.EqualTo(HandshakeMode.None));
        });
    }

    [Test]
    public void 全量参数_可自由组合()
    {
        var config = new PortConfig
        {
            BaudRate = 921600,
            DataBits = 7,
            StopBits = StopBitsMode.OnePointFive,
            Parity = ParityMode.Even,
            Handshake = HandshakeMode.RequestToSend,
        };

        Assert.Multiple(() =>
        {
            Assert.That(config.BaudRate, Is.EqualTo(921600));
            Assert.That(config.DataBits, Is.EqualTo(7));
            Assert.That(config.StopBits, Is.EqualTo(StopBitsMode.OnePointFive));
            Assert.That(config.Parity, Is.EqualTo(ParityMode.Even));
            Assert.That(config.Handshake, Is.EqualTo(HandshakeMode.RequestToSend));
        });
    }

    [Test]
    public void 参数相同_两实例相等_用于判断是否需重启会话()
    {
        var a = new PortConfig { BaudRate = 115200, Parity = ParityMode.Odd };
        var b = new PortConfig { BaudRate = 115200, Parity = ParityMode.Odd };

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void JSON往返_序列化再反序列化_值不变()
    {
        var original = new PortConfig
        {
            PortName = "COM7",
            BaudRate = 230400,
            DataBits = 6,
            StopBits = StopBitsMode.Two,
            Parity = ParityMode.Mark,
            Handshake = HandshakeMode.XOnXOff,
        };

        var json = PortConfig.ToJson(original);
        var restored = PortConfig.FromJson(json);

        Assert.That(restored, Is.EqualTo(original));
    }

    [Test]
    public void PortName_参与值相等_换口视为配置变化()
    {
        var a = new PortConfig { PortName = "COM5", BaudRate = 9600 };
        var b = new PortConfig { PortName = "COM6", BaudRate = 9600 };

        Assert.That(a, Is.Not.EqualTo(b));
    }

    // ---------- 边界场景 ----------

    [TestCase(1200)]
    [TestCase(9600)]
    [TestCase(115200)]
    [TestCase(921600)]
    [TestCase(3000000)]
    public void 波特率_标准值与自定义值_均合法(int baud)
    {
        var config = new PortConfig { BaudRate = baud };

        Assert.That(config.Validate(), Is.Empty);
    }

    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public void 数据位_5到8_均合法(int dataBits)
    {
        var config = new PortConfig { DataBits = dataBits };

        Assert.That(config.Validate(), Is.Empty);
    }

    [Test]
    public void 枚举字段_全部合法值_校验通过()
    {
        foreach (var stop in new[] { StopBitsMode.One, StopBitsMode.OnePointFive, StopBitsMode.Two })
        foreach (var parity in new[] { ParityMode.None, ParityMode.Odd, ParityMode.Even, ParityMode.Mark, ParityMode.Space })
        foreach (var hs in new[] { HandshakeMode.None, HandshakeMode.RequestToSend, HandshakeMode.XOnXOff, HandshakeMode.RequestToSendXOnXOff })
        {
            var config = new PortConfig { StopBits = stop, Parity = parity, Handshake = hs };
            Assert.That(config.Validate(), Is.Empty, () => $"{stop}/{parity}/{hs} 应合法");
        }
    }

    // ---------- 异常场景 ----------

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-9600)]
    public void 波特率_非正数_校验报错(int baud)
    {
        var config = new PortConfig { BaudRate = baud };

        Assert.That(config.Validate(), Is.Not.Empty);
    }

    [TestCase(4)]
    [TestCase(9)]
    [TestCase(0)]
    [TestCase(-8)]
    public void 数据位_越界_校验报错(int dataBits)
    {
        var config = new PortConfig { DataBits = dataBits };

        Assert.That(config.Validate(), Is.Not.Empty);
    }

    [Test]
    public void FromJson_损坏的JSON_返回null不抛异常()
    {
        Assert.That(PortConfig.FromJson("这不是JSON{{{"), Is.Null);
    }

    [Test]
    public void FromJson_null_返回null()
    {
        Assert.That(PortConfig.FromJson(null), Is.Null);
    }
}
