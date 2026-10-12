using System.IO.Ports;
using SerialAssist.Core;

namespace SerialAssist.Transport;

/// <summary>
/// ITransport 的 SerialPort 真实实现。注意:
/// - DataReceived 在线程池线程触发,原样上抛,由会话/管线决定线程边界;
/// - 收发过程中的 IOException(拔线)经 FatalError 上报,会话转 Error 态。
/// </summary>
public sealed class SerialPortTransport : ITransport, ITransportErrorReporter, IDisposable
{
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen ?? false;

    public event Action<byte[]>? DataReceived;
    public event Action<IOException>? FatalError;

    public void Open(PortConfig config)
    {
        var port = new SerialPort(config.PortName, config.BaudRate, TransportMapper.ToParity(config.Parity), config.DataBits, TransportMapper.ToStopBits(config.StopBits))
        {
            Handshake = TransportMapper.ToHandshake(config.Handshake),
            ReadBufferSize = 64 * 1024,
            WriteBufferSize = 64 * 1024,
            // 写超时必须有限(2s,与主流串口工具对齐):流控(CTS 被拉低/XOn 未收到)卡住时,
            // 无限等待会把 UI 线程钉死(手动/连发在 UI 线程同步发送)、循环发送永远停不下来
            WriteTimeout = 2000,
            // 与 SSCOM 等主流工具对齐:默认拉高 DTR/RTS。
            // .NET 默认 false,部分设备检测到 DTR/RTS 未使能会直接忽略收到的数据
            DtrEnable = true,
            RtsEnable = true,
        };

        SerialPort? oldPort = null;
        try
        {
            port.Open();
            port.DataReceived += OnDataReceived;
            oldPort = _port;
            _port = port;
        }
        catch
        {
            port.Dispose();
            throw;
        }
        finally
        {
            // 新口就位后释放旧口(改参数重启场景)
            oldPort?.Dispose();
        }
    }

    public void Close()
    {
        var port = _port;
        _port = null;
        if (port is null)
        {
            return;
        }
        try
        {
            port.DataReceived -= OnDataReceived;
            port.Close();
        }
        catch (IOException)
        {
            // 拔线后 Close 可能抛 IO 异常,口已失效,继续释放
        }
        finally
        {
            port.Dispose();
        }
    }

    public bool Send(byte[] data)
    {
        var port = _port;
        if (port is null || !port.IsOpen)
        {
            return false;
        }
        try
        {
            port.Write(data, 0, data.Length);
            return true;
        }
        catch (IOException ex)
        {
            FatalError?.Invoke(ex);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            // 口被并发关闭(竞态),同拔线处理
            FatalError?.Invoke(new IOException($"端口已关闭:{ex.Message}", ex));
            return false;
        }
        catch (TimeoutException)
        {
            // 写超时=被流控阻塞等可恢复状况:只报本次发送失败,不算致命错误,会话保持 Open
            return false;
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null)
        {
            return;
        }
        try
        {
            var count = port.BytesToRead;
            while (count > 0)
            {
                var buffer = new byte[count];
                var read = port.Read(buffer, 0, count);
                if (read > 0)
                {
                    DataReceived?.Invoke(read == count ? buffer : buffer[..read]);
                }
                count = port.BytesToRead;
            }
        }
        catch (IOException ex)
        {
            FatalError?.Invoke(ex);
        }
        catch (OperationCanceledException)
        {
            // 口被关闭,正常退出
        }
        catch (InvalidOperationException)
        {
            // 口在读过程中被关闭,正常退出
        }
    }

    public void Dispose() => Close();
}
