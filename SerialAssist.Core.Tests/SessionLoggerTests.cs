using System.Text;
using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// SessionLogger 规格测试(S7,2026-10-10 用户修订):日志默认**文本**可读,
/// 每条一行 "[HH:mm:ss.fff] 内容";需要 HEX 时显式注入 HexRenderer。
/// </summary>
[TestFixture]
public class SessionLoggerTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _path = Path.Combine(Path.GetTempPath(), $"sessionlog-{Guid.NewGuid():N}.log");
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_path);
    }

    [Test]
    public void Log_默认文本渲染_ASCII数据()
    {
        using (var logger = new SessionLogger(_path, append: false))
        {
            logger.Log(new DateTimeOffset(2026, 10, 10, 12, 30, 5, 123, TimeSpan.Zero), "Hi"u8.ToArray());
        }

        Assert.That(File.ReadAllLines(_path)[0], Is.EqualTo("[12:30:05.123] Hi"));
    }

    [Test]
    public void Log_默认文本渲染_UTF8中文可读()
    {
        using (var logger = new SessionLogger(_path, append: false))
        {
            logger.Log(new DateTimeOffset(2026, 10, 10, 12, 30, 5, 123, TimeSpan.Zero), "中文OK"u8.ToArray());
        }

        Assert.That(File.ReadAllLines(_path)[0], Is.EqualTo("[12:30:05.123] 中文OK"));
    }

    [Test]
    public void Log_注入HexRenderer_输出HEX格式()
    {
        using (var logger = new SessionLogger(_path, append: false, renderer: new HexRenderer()))
        {
            logger.Log(new DateTimeOffset(2026, 10, 10, 12, 30, 5, 456, TimeSpan.Zero), new byte[] { 0xAA, 0x0B });
        }

        Assert.That(File.ReadAllLines(_path)[0], Is.EqualTo("[12:30:05.456] AA 0B"));
    }

    [Test]
    public void Log_空数据_不产生行()
    {
        using (var logger = new SessionLogger(_path, append: false))
        {
            logger.Log(DateTimeOffset.Now, Array.Empty<byte>());
        }

        Assert.That(File.ReadAllText(_path), Is.Empty);
    }

    [Test]
    public void Append模式_保留原内容_续写()
    {
        File.WriteAllText(_path, "header\n");
        using (var logger = new SessionLogger(_path, append: true))
        {
            logger.Log(DateTimeOffset.Now, "x"u8.ToArray());
        }

        var lines = File.ReadAllLines(_path);
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("header"));
            Assert.That(lines, Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void Dispose后Log_静默不写不抛()
    {
        var logger = new SessionLogger(_path, append: false);
        logger.Dispose();

        Assert.DoesNotThrow(() => logger.Log(DateTimeOffset.Now, "x"u8.ToArray()));
        Assert.That(File.ReadAllLines(_path), Is.Empty);
    }

    [Test]
    public void Dispose两次_幂等不抛()
    {
        var logger = new SessionLogger(_path, append: false);
        logger.Dispose();

        Assert.DoesNotThrow(logger.Dispose);
    }

    [Test]
    public void 构造_父目录不存在_创建目录()
    {
        var deep = Path.Combine(Path.GetTempPath(), $"logdir-{Guid.NewGuid():N}", "sub");
        try
        {
            using (var logger = new SessionLogger(Path.Combine(deep, "a.log"), append: false))
            {
                logger.Log(DateTimeOffset.Now, "x"u8.ToArray());
            }

            Assert.That(File.Exists(Path.Combine(deep, "a.log")), Is.True);
        }
        finally
        {
            if (Directory.Exists(deep))
            {
                Directory.Delete(deep, recursive: true);
            }
        }
    }
}
