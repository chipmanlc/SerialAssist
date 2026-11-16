using NUnit.Framework;

namespace SerialAssist.Core.Tests;

[TestFixture]
public sealed class FramePersistenceTests
{
    private string _dir = null!;
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SerialAssistFrameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "frames.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Test]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var original = new[]
        {
            new DataFrame("读取版本", [0x41, 0x54, 0x2B], ChecksumMode.Crc16Modbus, 250)
            {
                Enabled = false,
                SourceInput = "AT+",
                InputMode = FrameInputMode.Text,
            },
            new DataFrame("复位", [0x01, 0x02], ChecksumMode.None, 0),
        };

        Assert.That(FramePersistence.Save(_path, original), Is.True);
        var loaded = FramePersistence.Load(_path);

        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(loaded[0].Name, Is.EqualTo("读取版本"));
            Assert.That(loaded[0].Payload, Is.EqualTo(new byte[] { 0x41, 0x54, 0x2B }));
            Assert.That(loaded[0].Checksum, Is.EqualTo(ChecksumMode.Crc16Modbus));
            Assert.That(loaded[0].DelayAfterMs, Is.EqualTo(250));
            Assert.That(loaded[0].Enabled, Is.False);
            Assert.That(loaded[0].SourceInput, Is.EqualTo("AT+"));
            Assert.That(loaded[0].InputMode, Is.EqualTo(FrameInputMode.Text));
            Assert.That(loaded[1].Name, Is.EqualTo("复位"));
        });
    }

    [Test]
    public void SaveThenLoad_PreservesTextEscapesAsBytes()
    {
        var frame = new DataFrame("带转义", [0x41, 0x54, 0x0D, 0x0A])
        {
            SourceInput = "AT\\r\\n",
            InputMode = FrameInputMode.Text,
        };

        Assert.That(FramePersistence.Save(_path, new[] { frame }), Is.True);
        var loaded = FramePersistence.Load(_path).Single();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.SourceInput, Is.EqualTo("AT\\r\\n"));
            Assert.That(loaded.InputMode, Is.EqualTo(FrameInputMode.Text));
            Assert.That(loaded.BuildBytes(), Is.EqualTo(new byte[] { 0x41, 0x54, 0x0D, 0x0A }));
        });
    }

    [Test]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.That(FramePersistence.Load(_path), Is.Empty);
    }

    [Test]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.That(FramePersistence.Load(_path), Is.Empty);
    }

    [Test]
    public void Changed_FiresOnMutations()
    {
        var store = new FrameStore();
        var count = 0;
        store.Changed += (_, _) => count++;

        var frame = new DataFrame("A", [0x01]);
        store.Add(frame);
        store.Replace(0, new DataFrame("B", [0x02]));
        store.MoveDown(0);
        store.RemoveAt(0);

        Assert.That(count, Is.EqualTo(3));
    }
}