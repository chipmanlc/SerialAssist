using System.Text;

namespace SerialAssist.Core;

/// <summary>
/// 数据帧文本输入解析(2026-10-12 用户新增):C 风格转义 → 字节。
/// 转义集:\r \n \t \0 \a \b \f \v \\ \xHH(1~2 位十六进制,超 2 位拒绝防歧义);
/// 其余字符按注入编码(GBK 等)编码。非法转义经 error 报出,不抛异常。
/// 与 HexCodec 并列:HEX 模式走 HexCodec,文本模式走本类,产物都是精确字节。
/// </summary>
public static class TextEscapeCodec
{
    public static bool TryParse(string? input, Encoding encoding, out byte[] value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = Array.Empty<byte>();
            error = "输入为空";
            return false;
        }

        var bytes = new List<byte>(input.Length);
        var text = new StringBuilder();

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c != '\\')
            {
                text.Append(c);
                continue;
            }

            // 转义序列:先把前面累积的普通文本按编码落成字节,再处理转义
            FlushText(text, encoding, bytes);
            if (i + 1 >= input.Length)
            {
                value = Array.Empty<byte>();
                error = "末尾是孤立的反斜杠,转义序列不完整";
                return false;
            }

            var next = input[++i];
            switch (next)
            {
                case 'r': bytes.Add(0x0D); break;
                case 'n': bytes.Add(0x0A); break;
                case 't': bytes.Add(0x09); break;
                case '0': bytes.Add(0x00); break;
                case 'a': bytes.Add(0x07); break;
                case 'b': bytes.Add(0x08); break;
                case 'f': bytes.Add(0x0C); break;
                case 'v': bytes.Add(0x0B); break;
                case '\\': bytes.Add(0x5C); break;
                case 'x' or 'X':
                    if (!TryParseHexEscape(input, ref i, out var b, out error))
                    {
                        value = Array.Empty<byte>();
                        return false;
                    }
                    bytes.Add(b);
                    break;
                default:
                    value = Array.Empty<byte>();
                    error = $"未知转义序列:\\{next}";
                    return false;
            }
        }

        FlushText(text, encoding, bytes);
        value = bytes.ToArray();
        error = null;
        return true;
    }

    private static void FlushText(StringBuilder text, Encoding encoding, List<byte> bytes)
    {
        if (text.Length > 0)
        {
            bytes.AddRange(encoding.GetBytes(text.ToString()));
            text.Clear();
        }
    }

    private static bool TryParseHexEscape(string input, ref int i, out byte value, out string? error)
    {
        value = 0;
        error = null;
        var digits = 0;
        while (digits < 2 && i + 1 < input.Length && Uri.IsHexDigit(input[i + 1]))
        {
            i++;
            digits++;
            value = (byte)((value << 4) + Uri.FromHex(input[i]));
        }
        if (digits == 0)
        {
            error = "\\x 后需要 1~2 位十六进制数字";
            return false;
        }
        // 第 3 位还是十六进制数字 → 用户意图有歧义(\x12AB),拒绝而不是静默截断
        if (i + 1 < input.Length && Uri.IsHexDigit(input[i + 1]))
        {
            error = "\\x 后最多 2 位十六进制,多余位数请拆成独立 \\x";
            return false;
        }
        return true;
    }
}
