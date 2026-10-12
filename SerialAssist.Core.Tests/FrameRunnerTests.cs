using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// DataFrame 规格测试(设计文档 S6):payload + 可选校验附加(Sum8/CRC16 Modbus)。
/// CRC16 Modbus 校验向量:ASCII "123456789" → 0x4B37,帧内低字节在前;
/// CRC-8(多项式 0x07,初值 0)校验向量:"123456789" → 0xF4。
/// </summary>
[TestFixture]
public class DataFrameTests
{
    [Test]
    public void BuildBytes_无校验_仅payload()
    {
        var frame = new DataFrame("门指令", new byte[] { 0x01, 0x02 });

        Assert.That(frame.BuildBytes(), Is.EqualTo(new byte[] { 0x01, 0x02 }));
    }

    [Test]
    public void BuildBytes_Sum8_附加累加和()
    {
        var frame = new DataFrame("和校验", new byte[] { 0x01, 0x02 }, ChecksumMode.Sum8);

        Assert.That(frame.BuildBytes(), Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
    }

    [Test]
    public void BuildBytes_Sum8_溢出取低8位()
    {
        var frame = new DataFrame("溢出", new byte[] { 0xFF, 0x01 }, ChecksumMode.Sum8);

        Assert.That(frame.BuildBytes(), Is.EqualTo(new byte[] { 0xFF, 0x01, 0x00 }));
    }

    [Test]
    public void BuildBytes_Crc16Modbus_低字节在前_标准向量()
    {
        var frame = new DataFrame("CRC16", "123456789"u8.ToArray(), ChecksumMode.Crc16Modbus);

        // 校验值 0x4B37 → 帧内 L=0x37, H=0x4B
        Assert.That(frame.BuildBytes()[^2..], Is.EqualTo(new byte[] { 0x37, 0x4B }));
    }

    [Test]
    public void BuildBytes_Crc8_标准向量()
    {
        var frame = new DataFrame("CRC8", "123456789"u8.ToArray(), ChecksumMode.Crc8);

        Assert.That(frame.BuildBytes()[^1], Is.EqualTo(0xF4));
    }

    [Test]
    public void BuildBytes_空payload加Sum8_仅校验字节()
    {
        var frame = new DataFrame("空帧", Array.Empty<byte>(), ChecksumMode.Sum8);

        Assert.That(frame.BuildBytes(), Is.EqualTo(new byte[] { 0x00 }));
    }
}

/// <summary>
/// FrameRunner 规格测试(设计文档 S6):单发/选中连发(帧间延时)/循环发送(可取消);
/// 发送失败立即停止,不空转;取消在延时期间也能及时退出。
/// </summary>
[TestFixture]
public class FrameRunnerTests
{
    private List<byte[]> _sent = null!;
    private FrameRunner _runner = null!;

    [SetUp]
    public void SetUp()
    {
        _sent = new List<byte[]>();
        _runner = new FrameRunner(bytes =>
        {
            _sent.Add(bytes);
            return true;
        });
    }

    // ---------- 核心场景 ----------

    [Test]
    public async Task SendOnce_单帧_发送构建后的完整帧()
    {
        var frame = new DataFrame("F1", new byte[] { 0x01 }, ChecksumMode.Sum8);

        var ok = await _runner.SendOnceAsync(frame);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(_sent, Has.Count.EqualTo(1));
            Assert.That(_sent[0], Is.EqualTo(new byte[] { 0x01, 0x01 }));
        });
    }

    [Test]
    public async Task SendSelected_两帧_按顺序发送且遵守帧间延时()
    {
        var f1 = new DataFrame("F1", new byte[] { 0x01 }) { DelayAfterMs = 60 };
        var f2 = new DataFrame("F2", new byte[] { 0x02 });
        var timestamps = new List<long>();

        _runner.Sent += _ => timestamps.Add(Stopwatch.GetTimestamp());
        var ok = await _runner.SendSelectedAsync(new[] { f1, f2 });

        var gapMs = (timestamps[1] - timestamps[0]) * 1000.0 / Stopwatch.Frequency;
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(_sent, Has.Count.EqualTo(2));
            Assert.That(gapMs, Is.GreaterThanOrEqualTo(40), "帧间延时 60ms 应生效");
        });
    }

    [Test]
    public async Task LoopAsync_循环发送_直到取消()
    {
        var frame = new DataFrame("F", new byte[] { 0x01 }) { DelayAfterMs = 10 };
        using var cts = new CancellationTokenSource();

        var loop = _runner.LoopAsync(new[] { frame }, cts.Token);
        await Task.Delay(120);
        cts.Cancel();
        await loop;

        Assert.That(_sent.Count, Is.GreaterThanOrEqualTo(2), "至少发完一轮以上");
    }

    // ---------- 边界场景 ----------

    [Test]
    public async Task SendSelected_空列表_返回true不发()
    {
        var ok = await _runner.SendSelectedAsync(Array.Empty<DataFrame>());

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(_sent, Is.Empty);
        });
    }

    [Test]
    public async Task LoopAsync_取消发生在第一帧前_一帧不发()
    {
        var frame = new DataFrame("F", new byte[] { 0x01 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await _runner.LoopAsync(new[] { frame }, cts.Token);

        Assert.That(_sent, Is.Empty);
    }

    // ---------- 异常场景 ----------

    [Test]
    public async Task SendOnce_发送失败_返回false()
    {
        var runner = new FrameRunner(_ => false);

        var ok = await runner.SendOnceAsync(new DataFrame("F", new byte[] { 0x01 }));

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task SendSelected_中途发送失败_立即停止不继续()
    {
        var failRunner = new FrameRunner(bytes =>
        {
            if (_sent.Count == 0)
            {
                _sent.Add(bytes); // 第一帧成功
                return true;
            }
            return false; // 之后全失败
        });
        var f1 = new DataFrame("F1", new byte[] { 0x01 });
        var f2 = new DataFrame("F2", new byte[] { 0x02 });
        var f3 = new DataFrame("F3", new byte[] { 0x03 });

        var ok = await failRunner.SendSelectedAsync(new[] { f1, f2, f3 });

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(_sent, Has.Count.EqualTo(1), "失败后不应再发后续帧");
        });
    }

    [Test]
    public async Task LoopAsync_发送失败_循环退出()
    {
        var failRunner = new FrameRunner(_ => false);
        var frame = new DataFrame("F", new byte[] { 0x01 }) { DelayAfterMs = 5 };

        await failRunner.LoopAsync(new[] { frame }, CancellationToken.None);

        Assert.That(_sent, Is.Empty);
    }
}
