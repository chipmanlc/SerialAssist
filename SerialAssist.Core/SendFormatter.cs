using System.Text;

namespace SerialAssist.Core;

public enum SendMode
{
    Text,
    Hex,
}

/// <summary>发送行尾:None=不加,CR=\r,LF=\n,CRLF=\r\n(2026-10-10 修订:默认 CR,多数 CLI 设备只认回车)。</summary>
public enum NewlineMode
{
    None,
    CR,
    LF,
    CRLF,
}

/// <summary>格式化结果:Ok=false 时 Error 给出可显示原因,Bytes 恒为非 null。</summary>
public readonly record struct SendPayload(bool Ok, byte[] Bytes, string? Error);

/// <summary>
/// 发送格式化(S3 发送侧):文本/HEX 双模式,行尾可选(默认 CR)。
/// 行尾只作用于文本模式:HEX 是字节级精确构造(协议帧)场景,附加行尾会污染二进制帧。
/// HEX 复用 HexCodec 容忍规则;空输入报错不发。
/// </summary>
public static class SendFormatter
{
    /// <summary>默认行尾 CR:登入类 CLI 设备(admin\r)普遍只认回车,多一个 \n 会被当空输入。</summary>
    public const NewlineMode DefaultNewline = NewlineMode.CR;

    public static SendPayload Format(SendMode mode, string? input, Encoding? encoding = null, NewlineMode newline = DefaultNewline)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SendPayload(false, Array.Empty<byte>(), "发送内容为空");
        }

        byte[] bytes;
        switch (mode)
        {
            case SendMode.Hex:
                if (!HexCodec.TryParse(input, out var parsed, out var error))
                {
                    return new SendPayload(false, Array.Empty<byte>(), error);
                }
                bytes = parsed;
                break;
            case SendMode.Text:
                bytes = (encoding ?? Encoding.ASCII).GetBytes(input);
                break;
            default:
                return new SendPayload(false, Array.Empty<byte>(), $"未知发送模式:{mode}");
        }

        // 行尾只掺入文本模式;HEX 模式字节必须与用户输入逐字节一致
        if (mode == SendMode.Text)
        {
            bytes = newline switch
            {
                NewlineMode.CR => [.. bytes, 0x0D],
                NewlineMode.LF => [.. bytes, 0x0A],
                NewlineMode.CRLF => [.. bytes, 0x0D, 0x0A],
                _ => bytes,
            };
        }
        return new SendPayload(true, bytes, null);
    }
}
