using System.Collections.ObjectModel;
using System.Text;

namespace SerialAssist.Core;

/// <summary>一条接收记录:到达时刻(毫秒精度) + 原始字节块。</summary>
public readonly record struct ReceivedEntry(DateTimeOffset Timestamp, byte[] Bytes);

/// <summary>
/// 接收管线(设计文档关键点1/4):模型层无条件追加(冻结是视图层的事);
/// 容量背压丢最旧并累计丢弃字节;每次 Push 触发 Updated(UI 负责节流渲染)。
/// 线程安全:Push 来自串口线程,Snapshot/Clear 来自 UI 线程,用锁保护。
/// </summary>
public sealed class ReceivePipeline
{
    public const int DefaultCapacity = 10_000;

    private readonly object _gate = new();
    private readonly List<ReceivedEntry> _entries = new();

    public int Capacity { get; set; } = DefaultCapacity;

    /// <summary>因背压被丢弃的字节总数(UI 显示"已丢弃 N 字节")。Clear 时归零。</summary>
    public long DroppedByteCount { get; private set; }

    /// <summary>
    /// 累计推送条数(含已被背压淘汰的),Clear 时归零。
    /// 消费方(视图/日志)用它替代"列表下标"定位新数据——列表下标在背压丢最旧后
    /// 会追平 Count 导致消费方永久假死(P5 压力固证)。
    /// </summary>
    public long TotalEntries { get; private set; }

    /// <summary>当前快照中首条的序列号 = TotalEntries - Count;空管线为 0。(锁内读,与 Push/Clear 一致)</summary>
    public long FirstSequence
    {
        get
        {
            lock (_gate)
            {
                return TotalEntries - _entries.Count;
            }
        }
    }

    public event Action? Updated;

    public void Push(byte[] chunk, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _entries.Add(new ReceivedEntry(timestamp ?? DateTimeOffset.Now, chunk));
            TotalEntries++;
            while (_entries.Count > Capacity && _entries.Count > 0)
            {
                DroppedByteCount += _entries[0].Bytes.Length;
                _entries.RemoveAt(0);
            }
        }
        Updated?.Invoke();
    }

    /// <summary>当前全部条目的快照(副本,遍历期间 Push 安全)。</summary>
    public IReadOnlyList<ReceivedEntry> Snapshot()
    {
        lock (_gate)
        {
            return new ReadOnlyCollection<ReceivedEntry>(_entries.ToArray());
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            DroppedByteCount = 0;
            TotalEntries = 0;
        }
        Updated?.Invoke();
    }
}

/// <summary>字节源的一种显示投影(文本/HEX/未来更多)。无状态,可复用。</summary>
public interface IByteRenderer
{
    string Render(byte[] bytes);
}

/// <summary>HEX 投影:大写两位、空格分隔,委托 HexCodec。</summary>
public sealed class HexRenderer : IByteRenderer
{
    public string Render(byte[] bytes) => HexCodec.ToHexString(bytes);
}

/// <summary>文本投影:按指定编码解码;除 \r \n \t 外的控制字符显示为 '.'。</summary>
public sealed class TextRenderer : IByteRenderer
{
    private readonly Encoding _encoding;

    public TextRenderer(Encoding? encoding = null)
    {
        _encoding = encoding ?? Encoding.ASCII;
    }

    public string Render(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var decoded = _encoding.GetString(bytes);
        // string.Create 走堆,不受栈大小限制(大块数据也不会溢出)
        return string.Create(decoded.Length, decoded, static (buf, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                buf[i] = char.IsControl(c) && c is not ('\r' or '\n' or '\t') ? '.' : c;
            }
        });
    }
}
