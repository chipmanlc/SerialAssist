using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SerialAssist.Core;

/// <summary>数据帧持久化:保存到 AppPaths 数据目录,启动时恢复;损坏文件按空列表处理。</summary>
public static class FramePersistence
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<DataFrame> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        try
        {
            var frames = JsonSerializer.Deserialize<List<DataFrame>>(File.ReadAllText(path, Encoding.UTF8), Options);
            return frames?.Where(frame => frame is not null).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // 配置文件损坏或不可读时不阻塞启动;保留原文件,用户可手动检查。
            return [];
        }
    }

    public static bool Save(string path, IEnumerable<DataFrame> frames)
    {
        var tempPath = path + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var json = JsonSerializer.Serialize(frames.Where(frame => frame is not null).ToList(), Options);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 清理临时文件失败不影响主流程。
            }
            return false;
        }
    }
}