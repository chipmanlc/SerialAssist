using System.Text.Json;
using System.Text.Json.Serialization;

namespace SerialAssist.Core;

public enum StopBitsMode
{
    One,
    OnePointFive,
    Two,
}

public enum ParityMode
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

public enum HandshakeMode
{
    None,
    RequestToSend,
    XOnXOff,
    RequestToSendXOnXOff,
}

/// <summary>
/// 串口参数(设计文档 S2)。core 自定义枚举,与 System.IO.Ports 的映射留给传输层。
/// record 值相等语义:两实例相等 = 参数未变,会话无需重启。
/// </summary>
public record PortConfig
{
    [JsonPropertyName("portName")]
    public string PortName { get; init; } = string.Empty;

    [JsonPropertyName("baudRate")]
    public int BaudRate { get; init; } = 9600;

    [JsonPropertyName("dataBits")]
    public int DataBits { get; init; } = 8;

    [JsonPropertyName("stopBits")]
    public StopBitsMode StopBits { get; init; } = StopBitsMode.One;

    [JsonPropertyName("parity")]
    public ParityMode Parity { get; init; } = ParityMode.None;

    [JsonPropertyName("handshake")]
    public HandshakeMode Handshake { get; init; } = HandshakeMode.None;

    /// <summary>非法值集合,空即合法。非法值只报错,不允许构造期抛异常(UI 需要逐字段报错)。</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (BaudRate <= 0)
        {
            errors.Add($"波特率必须为正数,当前 {BaudRate}");
        }
        if (DataBits is < 5 or > 8)
        {
            errors.Add($"数据位须在 5~8 之间,当前 {DataBits}");
        }
        return errors;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string ToJson(PortConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static PortConfig? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<PortConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
