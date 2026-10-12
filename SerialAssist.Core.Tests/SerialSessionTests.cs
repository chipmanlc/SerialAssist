using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// SerialSession 规格测试(设计文档 S1/S2/关键点5):
/// 状态机 Closed→Opening→Open→Error;打开失败转 Error 不抛;
/// 改参数=安全重启,新配置打不开回滚旧配置;幂等 Close;未开发送不崩溃。
/// </summary>
[TestFixture]
public class SerialSessionTests
{
    private FakeTransport _transport = null!;
    private SerialSession _session = null!;

    /// <summary>标准测试配置:带端口名(新规格 Open 要求非空 PortName)。</summary>
    private static PortConfig Cfg(int baud = 9600) => new() { PortName = "COM5", BaudRate = baud };

    [SetUp]
    public void SetUp()
    {
        _transport = new FakeTransport();
        _session = new SerialSession(_transport);
    }

    [TearDown]
    public void TearDown()
    {
        _session.Dispose();
    }

    // ---------- 核心场景 ----------

    [Test]
    public void Open_合法配置_进入Open态且配置生效()
    {
        var config = new PortConfig { PortName = "COM5", BaudRate = 115200 };

        _session.Open(config);

        Assert.Multiple(() =>
        {
            Assert.That(_session.State, Is.EqualTo(SessionState.Open));
            Assert.That(_session.Config, Is.EqualTo(config));
            Assert.That(_transport.OpenedConfigs, Has.Count.EqualTo(1));
            Assert.That(_transport.OpenedConfigs[0], Is.EqualTo(config));
        });
    }

    [Test]
    public void Open_状态变迁序列_Closed到Opening到Open()
    {
        var states = new List<SessionState>();
        _session.StateChanged += s => states.Add(s);

        _session.Open(Cfg());

        Assert.That(states, Is.EqualTo(new[] { SessionState.Opening, SessionState.Open }));
    }

    [Test]
    public void Close_从Open态_回到Closed()
    {
        _session.Open(Cfg());

        _session.Close();

        Assert.Multiple(() =>
        {
            Assert.That(_session.State, Is.EqualTo(SessionState.Closed));
            Assert.That(_transport.IsOpen, Is.False);
        });
    }

    [Test]
    public void Send_打开状态_数据到达传输层()
    {
        _session.Open(Cfg());
        var data = new byte[] { 0x01, 0x02 };

        var ok = _session.Send(data);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(_transport.Sent, Has.Count.EqualTo(1));
            Assert.That(_transport.Sent[0], Is.EqualTo(data));
        });
    }

    [Test]
    public void 传输层收到数据_会话事件转发同一内容()
    {
        byte[]? received = null;
        _session.DataReceived += bytes => received = bytes;
        _session.Open(Cfg());

        _transport.SimulateReceive(new byte[] { 0xAA, 0xBB });

        Assert.That(received, Is.EqualTo(new byte[] { 0xAA, 0xBB }));
    }

    [Test]
    public void ApplyConfig_打开状态下改参数_自动重启会话且仍为Open()
    {
        var oldConfig = new PortConfig { PortName = "COM5", BaudRate = 9600 };
        var newConfig = new PortConfig { PortName = "COM5", BaudRate = 115200 };
        _session.Open(oldConfig);

        var ok = _session.ApplyConfig(newConfig);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(_session.State, Is.EqualTo(SessionState.Open));
            Assert.That(_session.Config, Is.EqualTo(newConfig));
            // 重启 = close 一次旧 + open 两次(旧一次新一次)
            Assert.That(_transport.OpenedConfigs, Has.Count.EqualTo(2));
            Assert.That(_transport.OpenedConfigs[1], Is.EqualTo(newConfig));
        });
    }

    [Test]
    public void ApplyConfig_打开状态下配置未变_不重启()
    {
        var config = new PortConfig { PortName = "COM5", BaudRate = 9600 };
        _session.Open(config);

        var ok = _session.ApplyConfig(new PortConfig { PortName = "COM5", BaudRate = 9600 });

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(_transport.OpenedConfigs, Has.Count.EqualTo(1), "配置未变不应重启");
        });
    }

    // ---------- 异常场景 ----------

    [Test]
    public void Open_端口被占用_转Error态且不抛异常()
    {
        _transport.OpenException = new IOException("拒绝访问 COM5");

        Assert.DoesNotThrow(() => _session.Open(Cfg()));

        Assert.Multiple(() =>
        {
            Assert.That(_session.State, Is.EqualTo(SessionState.Error));
            Assert.That(_session.LastError, Does.Contain("COM5"));
        });
    }

    [Test]
    public void ApplyConfig_新配置打不开_回滚旧配置保持Open()
    {
        var oldConfig = new PortConfig { PortName = "COM5", BaudRate = 9600 };
        var newConfig = new PortConfig { PortName = "COM5", BaudRate = 115200 };
        _session.Open(oldConfig);
        // 模拟:115200 打不开,但 9600 旧配置可以重新打开
        _transport.OpenException = new IOException("波特率不支持");
        _transport.FailOnlyForBaudRate = 115200;

        var ok = _session.ApplyConfig(newConfig);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(_session.State, Is.EqualTo(SessionState.Open), "回滚成功应保持 Open");
            Assert.That(_session.Config, Is.EqualTo(oldConfig));
            Assert.That(_session.LastError, Is.Not.Null);
        });
    }

    [Test]
    public void ApplyConfig_新配置打不开且回滚也失败_转Error态()
    {
        var oldConfig = new PortConfig { PortName = "COM5", BaudRate = 9600 };
        _session.Open(oldConfig);
        _transport.OpenException = new IOException("设备消失");

        var ok = _session.ApplyConfig(new PortConfig { PortName = "COM5", BaudRate = 115200 });

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(_session.State, Is.EqualTo(SessionState.Error));
            Assert.That(_session.LastError, Is.Not.Null);
        });
    }

    [Test]
    public void Close_未打开时调用_幂等不抛()
    {
        Assert.DoesNotThrow(() => _session.Close());
        Assert.That(_session.State, Is.EqualTo(SessionState.Closed));
    }

    [Test]
    public void Close_连续调用两次_仍为Closed()
    {
        _session.Open(Cfg());

        _session.Close();
        _session.Close();

        Assert.That(_session.State, Is.EqualTo(SessionState.Closed));
    }

    [Test]
    public void Send_未打开_返回false不抛()
    {
        var ok = _session.Send(new byte[] { 0x01 });

        Assert.That(ok, Is.False);
    }

    [Test]
    public void Send_传输层抛InvalidOperationException_返回false转Error不穿透()
    {
        // 竞态场景:发送途中口被关闭,SerialPort.Write 抛 InvalidOperationException
        var throwingTransport = new ThrowingSendTransport(new InvalidOperationException("端口已关闭"));
        using var session = new SerialSession(throwingTransport);
        session.Open(new PortConfig { PortName = "COM5", BaudRate = 9600 });

        InvalidOperationException? escaped = null;
        bool ok = false;
        try
        {
            ok = session.Send(new byte[] { 0x01 });
        }
        catch (InvalidOperationException ex)
        {
            escaped = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(escaped, Is.Null, "异常不应穿透到调用方");
            Assert.That(ok, Is.False);
            Assert.That(session.State, Is.EqualTo(SessionState.Error));
        });
    }

    [Test]
    public void Open_非法配置_报错不触碰传输层()
    {
        var invalid = new PortConfig { PortName = "COM5", BaudRate = -1 };

        Assert.DoesNotThrow(() => _session.Open(invalid));

        Assert.Multiple(() =>
        {
            Assert.That(_session.State, Is.EqualTo(SessionState.Error));
            Assert.That(_transport.OpenedConfigs, Is.Empty);
        });
    }

    [Test]
    public void Open_空端口名_报错不触碰传输层()
    {
        var noName = new PortConfig { PortName = "", BaudRate = 9600 };

        Assert.DoesNotThrow(() => _session.Open(noName));

        Assert.Multiple(() =>
        {
            Assert.That(_session.State, Is.EqualTo(SessionState.Error));
            Assert.That(_session.LastError, Does.Contain("端口"));
            Assert.That(_transport.OpenedConfigs, Is.Empty);
        });
    }

    [Test]
    public void 传输层中途抛IOException_拔线场景_会话转Error()
    {
        _session.Open(Cfg());

        _transport.SimulateFatalError(new IOException("设备已拔出"));

        Assert.That(_session.State, Is.EqualTo(SessionState.Error));
    }

    [Test]
    public void Dispose_关闭会话_此后Send返回false()
    {
        _session.Open(Cfg());

        _session.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(_session.State, Is.EqualTo(SessionState.Closed));
            Assert.That(_session.Send(new byte[] { 0x01 }), Is.False);
        });
    }
}

/// <summary>Send 固定抛指定异常的假传输层。</summary>
internal sealed class ThrowingSendTransport(Exception sendException) : ITransport
{
    public bool IsOpen => true;
    public void Open(PortConfig config) { }
    public void Close() { }
    public bool Send(byte[] data) => throw sendException;
    public event Action<byte[]>? DataReceived
    {
        add { }
        remove { }
    }
}

/// <summary>可脚本化的假传输层:按测试需要抛异常/记录调用。</summary>
internal sealed class FakeTransport : ITransport, ITransportErrorReporter
{
    private Action<byte[]>? _dataReceived;

    public bool IsOpen { get; private set; }
    public List<PortConfig> OpenedConfigs { get; } = new();
    public List<byte[]> Sent { get; } = new();

    /// <summary>Open 失败时抛出的异常(null = 不失败)。</summary>
    public IOException? OpenException { get; set; }

    /// <summary>若设置,OpenException 仅对匹配该波特率的 Open 生效(用于"新配置失败、旧配置可回滚")。</summary>
    public int? FailOnlyForBaudRate { get; set; }

    public event Action<byte[]>? DataReceived
    {
        add => _dataReceived += value;
        remove => _dataReceived -= value;
    }

    public event Action<IOException>? FatalError;

    public void Open(PortConfig config)
    {
        var shouldFail = OpenException is not null
            && (FailOnlyForBaudRate is null || FailOnlyForBaudRate == config.BaudRate);
        // 失败的尝试也记录,便于断言重启/回滚各打了几次 Open
        OpenedConfigs.Add(config);
        if (shouldFail)
        {
            throw OpenException!;
        }
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public bool Send(byte[] data)
    {
        if (!IsOpen) return false;
        Sent.Add(data);
        return true;
    }

    public void SimulateReceive(byte[] bytes) => _dataReceived?.Invoke(bytes);

    /// <summary>模拟传输层自己坏了(拔线):经真实事件通道上报,由会话转 Error。</summary>
    public void SimulateFatalError(IOException error) => FatalError?.Invoke(error);
}
