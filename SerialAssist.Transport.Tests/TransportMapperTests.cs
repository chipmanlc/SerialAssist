using System.IO.Ports;
using NUnit.Framework;
using SerialAssist.Core;

namespace SerialAssist.Transport.Tests;

/// <summary>
/// TransportMapper 规格:core 枚举 → System.IO.Ports 枚举全组合一一对应。
/// </summary>
[TestFixture]
public class TransportMapperTests
{
    [TestCase(ParityMode.None, Parity.None)]
    [TestCase(ParityMode.Odd, Parity.Odd)]
    [TestCase(ParityMode.Even, Parity.Even)]
    [TestCase(ParityMode.Mark, Parity.Mark)]
    [TestCase(ParityMode.Space, Parity.Space)]
    public void 校验位映射_全组合(ParityMode from, Parity to)
    {
        Assert.That(TransportMapper.ToParity(from), Is.EqualTo(to));
    }

    [TestCase(StopBitsMode.One, StopBits.One)]
    [TestCase(StopBitsMode.OnePointFive, StopBits.OnePointFive)]
    [TestCase(StopBitsMode.Two, StopBits.Two)]
    public void 停止位映射_全组合(StopBitsMode from, StopBits to)
    {
        Assert.That(TransportMapper.ToStopBits(from), Is.EqualTo(to));
    }

    [TestCase(HandshakeMode.None, Handshake.None)]
    [TestCase(HandshakeMode.RequestToSend, Handshake.RequestToSend)]
    [TestCase(HandshakeMode.XOnXOff, Handshake.XOnXOff)]
    [TestCase(HandshakeMode.RequestToSendXOnXOff, Handshake.RequestToSendXOnXOff)]
    public void 流控映射_全组合(HandshakeMode from, Handshake to)
    {
        Assert.That(TransportMapper.ToHandshake(from), Is.EqualTo(to));
    }

    [Test]
    public void 未知枚举值_报ArgumentOutOfRange不静默映射()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransportMapper.ToParity((ParityMode)999));
    }
}
