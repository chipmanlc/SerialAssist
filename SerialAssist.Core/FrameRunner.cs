namespace SerialAssist.Core;

public enum ChecksumMode
{
    None,
    Sum8,
    Crc8,
    Crc16Modbus,
}

/// <summary>帧 payload 的输入形态(2026-10-12 用户新增):文本(含转义)或 HEX。仅用于编辑回显与显示,不影响上线字节。</summary>
public enum FrameInputMode
{
    Text,
    Hex,
}

/// <summary>
/// 一条自定义报文帧(设计文档 S6):名字 + payload + 校验附加 + 帧后延时。
/// BuildBytes 产出真正上线字节:payload 在前,校验字节在后。
/// SourceInput/InputMode 记录原始输入,重新编辑时回显原文(HEX↔文本转换有损,不反向推导)。
/// </summary>
public sealed class DataFrame
{
    public string Name { get; init; }
    public byte[] Payload { get; init; }
    public ChecksumMode Checksum { get; init; }

    /// <summary>原始输入文本(编辑框里的原样内容),重新编辑时回显;旧数据默认空。</summary>
    public string SourceInput { get; init; } = string.Empty;

    /// <summary>原始输入形态;旧数据默认 HEX。</summary>
    public FrameInputMode InputMode { get; init; } = FrameInputMode.Hex;

    /// <summary>发送本帧后等待的毫秒数(连发/循环时帧间间隔)。</summary>
    public int DelayAfterMs { get; init; }

    /// <summary>
    /// 启停开关(S6):连发/循环只处理启用帧(停用帧的延时也不等待);
    /// 单发是用户显式指定,不做此过滤。默认 true,旧构造不受影响。
    /// </summary>
    public bool Enabled { get; init; } = true;

    public DataFrame(string name, byte[] payload, ChecksumMode checksum = ChecksumMode.None, int delayAfterMs = 0)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Checksum = checksum;
        DelayAfterMs = Math.Max(0, delayAfterMs);
    }

    public byte[] BuildBytes()
    {
        return Checksum switch
        {
            ChecksumMode.None => (byte[])Payload.Clone(),
            ChecksumMode.Sum8 => [.. Payload, Checksums.Sum8(Payload)],
            ChecksumMode.Crc8 => [.. Payload, Checksums.Crc8(Payload)],
            ChecksumMode.Crc16Modbus => [.. Payload, .. Checksums.Crc16Modbus(Payload)],
            _ => (byte[])Payload.Clone(),
        };
    }
}

/// <summary>校验算法(CRC16 Modbus 低字节在前)。</summary>
public static class Checksums
{
    public static byte Sum8(ReadOnlySpan<byte> data)
    {
        byte sum = 0;
        foreach (var b in data)
        {
            sum += b;
        }
        return sum;
    }

    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        // CRC-8:多项式 0x07,初值 0x00,无反转
        byte crc = 0;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
            }
        }
        return crc;
    }

    /// <summary>返回 [低字节, 高字节](Modbus RTU 惯例)。</summary>
    public static byte[] Crc16Modbus(ReadOnlySpan<byte> data)
    {
        // CRC-16/MODBUS:多项式 0xA001(反转 0x8005),初值 0xFFFF
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                var lsb = (crc & 1) != 0;
                crc >>= 1;
                if (lsb)
                {
                    crc ^= 0xA001;
                }
            }
        }
        return [(byte)(crc & 0xFF), (byte)(crc >> 8)];
    }
}

/// <summary>
/// 帧调度器(设计文档关键点3):单发/选中连发(帧间 Task.Delay)/循环发送(可取消);
/// 发送失败立即停止,不空转。sender 由上层注入(通常是 session.Send)。
/// </summary>
public sealed class FrameRunner
{
    private readonly Func<byte[], bool> _sender;

    /// <summary>每帧实际上线时触发(参数 = 完整帧字节,供发送区回显/计数)。</summary>
    public event Action<byte[]>? Sent;

    public FrameRunner(Func<byte[], bool> sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public async Task<bool> SendOnceAsync(DataFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        return Send(frame.BuildBytes());
    }

    /// <summary>按顺序连发选中帧(跳过停用帧),遵守每帧 DelayAfterMs;任一帧失败立即停止。
    /// 入口先快照:调用方(UI)在 await 期间可能增删改帧,遍历活列表会竞态。</summary>
    public async Task<bool> SendSelectedAsync(IEnumerable<DataFrame> frames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var snapshot = frames.ToArray();
        foreach (var frame in snapshot)
        {
            if (!frame.Enabled)
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!Send(frame.BuildBytes()))
            {
                return false;
            }
            if (frame.DelayAfterMs > 0)
            {
                await Task.Delay(frame.DelayAfterMs, cancellationToken).ConfigureAwait(false);
            }
        }
        return true;
    }

    /// <summary>循环发送,直到取消或发送失败。取消时优雅返回,不抛异常。</summary>
    public async Task LoopAsync(IEnumerable<DataFrame> frames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var snapshot = frames.ToArray();
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var frame in snapshot)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                if (!frame.Enabled)
                {
                    continue;
                }
                if (!Send(frame.BuildBytes()))
                {
                    return;
                }
                if (frame.DelayAfterMs > 0)
                {
                    try
                    {
                        await Task.Delay(frame.DelayAfterMs, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }

    private bool Send(byte[] fullFrame)
    {
        var ok = _sender(fullFrame);
        if (ok)
        {
            Sent?.Invoke(fullFrame);
        }
        return ok;
    }
}
