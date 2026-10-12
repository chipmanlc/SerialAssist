namespace SerialAssist.Core;

/// <summary>
/// 应用数据目录决策:即插即用优先。
/// exe 旁边可写 → 用 &lt;exe目录&gt;/data(配置跟着U盘走,插哪用哪);
/// 不可写(如放进 Program Files / 只读介质)→ 退回 %LOCALAPPDATA%/SerialAssistPro/data。
/// 所有需要落盘的东西(崩溃日志、布局、参数记忆)统一从这里拿目录,禁止自行选址。
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> _dataDir = new(
        Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>当前生效的数据目录(已确保存在且可写)。</summary>
    public static string DataDir => _dataDir.Value;

    /// <summary>
    /// 启动时主动调用:立刻确定数据目录并确保存在可写,
    /// 让"exe 旁边不可写退回 LocalAppData"的决策在首帧前发生,而不是等第一次写文件才暴露。
    /// </summary>
    public static void EnsureCreated() => _ = _dataDir.Value;

    private static string Resolve()
    {
        // 单文件发布下 AppContext.BaseDirectory 就是 exe 所在目录,不是自解压临时目录
        var portableDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (IsWritable(portableDir))
            return portableDir;

        var fallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SerialAssistPro", "data");
        Directory.CreateDirectory(fallbackDir);
        return fallbackDir;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            // 探针带随机名:两个实例同时首启不至于互相踩
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
