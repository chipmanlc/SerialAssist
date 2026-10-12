using System.IO.Ports;
using SerialAssist.Core;

namespace SerialAssist.Transport;

/// <summary>core 枚举 → System.IO.Ports 枚举映射(设计文档:映射留在传输层)。</summary>
public static class TransportMapper
{
    public static Parity ToParity(ParityMode mode) => mode switch
    {
        ParityMode.None => Parity.None,
        ParityMode.Odd => Parity.Odd,
        ParityMode.Even => Parity.Even,
        ParityMode.Mark => Parity.Mark,
        ParityMode.Space => Parity.Space,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static StopBits ToStopBits(StopBitsMode mode) => mode switch
    {
        StopBitsMode.One => StopBits.One,
        StopBitsMode.OnePointFive => StopBits.OnePointFive,
        StopBitsMode.Two => StopBits.Two,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static Handshake ToHandshake(HandshakeMode mode) => mode switch
    {
        HandshakeMode.None => Handshake.None,
        HandshakeMode.RequestToSend => Handshake.RequestToSend,
        HandshakeMode.XOnXOff => Handshake.XOnXOff,
        HandshakeMode.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
