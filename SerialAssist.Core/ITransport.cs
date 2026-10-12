namespace SerialAssist.Core;

/// <summary>传输抽象:core 不直接依赖 System.IO.Ports,真实实现在传输层(SerialPortTransport)。</summary>
public interface ITransport
{
    bool IsOpen { get; }

    /// <summary>按配置打开。失败(占用/拔线/参数不支持)抛异常,由会话捕获转 Error 态。</summary>
    void Open(PortConfig config);

    void Close();

    /// <summary>发送字节。未打开或失败返回 false,不抛异常给调用方。</summary>
    bool Send(byte[] data);

    /// <summary>收到原始字节块(线程池线程触发,由会话决定如何上抛)。</summary>
    event Action<byte[]>? DataReceived;
}

/// <summary>传输层可选实现:上报自身致命错误(拔线/驱动异常),会话据此转 Error 态。</summary>
public interface ITransportErrorReporter
{
    event Action<IOException>? FatalError;
}
