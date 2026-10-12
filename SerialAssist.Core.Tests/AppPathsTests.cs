using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// AppPaths 契约测试:数据目录必须真实存在、当前进程可写、结果进程内稳定。
/// 即插即用承诺:exe 旁边可写 → data 目录跟随 exe;不可写 → 退回 LocalAppData。
/// </summary>
[TestFixture]
public class AppPathsTests
{
    // ---------- 核心场景 ----------

    [Test]
    public void DataDir_目录已创建_且当前进程可写()
    {
        var dir = AppPaths.DataDir;

        Assert.That(Directory.Exists(dir), Is.True, $"数据目录不存在: {dir}");

        var probe = Path.Combine(dir, ".write-probe-test");
        Assert.DoesNotThrow(() => File.WriteAllText(probe, "ok"),
            $"数据目录不可写: {dir}");
        File.Delete(probe);
    }

    [Test]
    public void DataDir_多次访问_返回同一路径()
    {
        Assert.That(AppPaths.DataDir, Is.EqualTo(AppPaths.DataDir));
    }
}
