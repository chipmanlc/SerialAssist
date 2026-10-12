namespace SerialAssist.Core;

public enum SessionState
{
    Closed,
    Opening,
    Open,
    Error,
}

/// <summary>
/// 单个串口会话状态机(设计文档关键点5):Closed→Opening→Open→Error。
/// 打开失败转 Error 不抛;改参数=安全重启,新配置失败回滚旧配置;
/// Close 幂等;未开发送返回 false。
/// </summary>
public sealed class SerialSession : IDisposable
{
    private readonly ITransport _transport;
    private SessionState _state = SessionState.Closed;

    public SerialSession(ITransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.DataReceived += bytes => DataReceived?.Invoke(bytes);
        // 传输层自身致命错误(拔线/驱动异常)→ 会话转 Error
        if (_transport is ITransportErrorReporter reporter)
        {
            reporter.FatalError += error => TransitionToError($"串口异常:{error.Message}");
        }
    }

    public SessionState State => _state;
    public PortConfig? Config { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>最近一次 Send 失败的原因(成功发送后清空);供 UI 给出准确提示而非一律"串口未打开"。</summary>
    public string? LastSendError { get; private set; }

    public event Action<SessionState>? StateChanged;
    public event Action<byte[]>? DataReceived;

    public void Open(PortConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>(config.Validate());
        if (string.IsNullOrWhiteSpace(config.PortName))
        {
            errors.Add("端口名为空,请先选择串口");
        }
        if (errors.Count > 0)
        {
            TransitionToError(string.Join("; ", errors));
            return;
        }

        SetState(SessionState.Opening);
        try
        {
            _transport.Open(config);
            Config = config;
            LastError = null;
            SetState(SessionState.Open);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            TransitionToError($"打开失败:{ex.Message}");
        }
    }

    public void Close()
    {
        if (_state is SessionState.Closed)
        {
            return;
        }
        try
        {
            _transport.Close();
        }
        finally
        {
            Config = null;
            SetState(SessionState.Closed);
        }
    }

    /// <summary>发送。未打开返回 false,不抛异常;失败原因写入 LastSendError。</summary>
    public bool Send(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_state != SessionState.Open)
        {
            LastSendError = "串口未打开";
            return false;
        }
        try
        {
            var ok = _transport.Send(data);
            LastSendError = ok ? null : "串口异常或发送超时(可能被流控阻塞)";
            return ok;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // IOException=拔线;InvalidOperationException=口被并发关闭(竞态),都不允许穿透到 UI
            LastSendError = $"串口异常:{ex.Message}";
            TransitionToError($"发送失败:{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 应用新配置。已打开时自动重启;新配置打不开则尝试回滚旧配置,
    /// 回滚成功保持 Open(返回 false),回滚也失败转 Error。
    /// 未打开时仅保存配置,返回 true。
    /// </summary>
    public bool ApplyConfig(PortConfig newConfig)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        if (_state != SessionState.Open)
        {
            Config = newConfig;
            return true;
        }
        if (Config == newConfig)
        {
            return true;
        }

        var oldConfig = Config;
        try
        {
            _transport.Close();
        }
        catch (IOException)
        {
            // 关闭失败不阻断重启尝试
        }

        try
        {
            _transport.Open(newConfig);
            Config = newConfig;
            LastError = null;
            SetState(SessionState.Open);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            LastError = $"应用新配置失败:{ex.Message}";
            if (oldConfig is not null)
            {
                try
                {
                    _transport.Open(oldConfig);
                    Config = oldConfig;
                    SetState(SessionState.Open);
                    return false;
                }
                catch (Exception rollbackEx) when (rollbackEx is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
                {
                    TransitionToError($"{LastError};回滚旧配置也失败:{rollbackEx.Message}");
                    return false;
                }
            }
            TransitionToError(LastError);
            return false;
        }
    }

    private void TransitionToError(string message)
    {
        LastError = message;
        SetState(SessionState.Error);
    }

    private void SetState(SessionState newState)
    {
        if (_state == newState)
        {
            return;
        }
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    public void Dispose() => Close();
}
