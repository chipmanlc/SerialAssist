using System.Text;

namespace SerialAssist.Core;

/// <summary>
/// 接收日志(S7,2026-10-10 用户修订):默认**文本**渲染、可读优先;
/// 需要 HEX 保真时显式注入 renderer。每条一行 "[HH:mm:ss.fff] 内容"。
/// AutoFlush 即写即落盘;Dispose 后静默不写。
/// </summary>
public sealed class SessionLogger : IDisposable
{
    private StreamWriter? _writer;
    private readonly IByteRenderer _renderer;

    public SessionLogger(string path, bool append = true, IByteRenderer? renderer = null)
    {
        _renderer = renderer ?? new TextRenderer(Encoding.UTF8);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _writer = new StreamWriter(path, append, Encoding.UTF8) { AutoFlush = true };
    }

    /// <summary>写一条。空数据跳过;已关闭或写失败返回 false,不抛异常。</summary>
    public bool Log(DateTimeOffset timestamp, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return true;
        }
        var writer = _writer;
        if (writer is null)
        {
            return false;
        }
        try
        {
            writer.Write('[');
            writer.Write(timestamp.ToString("HH:mm:ss.fff"));
            writer.Write("] ");
            writer.WriteLine(_renderer.Render(bytes));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        var writer = _writer;
        _writer = null;
        writer?.Dispose();
    }
}
