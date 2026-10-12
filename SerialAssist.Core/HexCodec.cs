using System.Globalization;

namespace SerialAssist.Core;

/// <summary>
/// HEX 字符串与字节数组互转(设计文档 S3)。
/// 输入容忍空格/逗号/0x 前缀/任意空白/大小写;非法输入不抛异常,经 error 报出。
/// </summary>
public static class HexCodec
{
    public static bool TryParse(string? input, out byte[] value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = Array.Empty<byte>();
            error = "输入为空";
            return false;
        }

        var span = input.AsSpan();
        // 先剥掉所有 0x 前缀与分隔符,得到纯十六进制位串再校验
        var sb = new System.Text.StringBuilder(span.Length);
        var i = 0;
        while (i < span.Length)
        {
            var c = char.ToUpperInvariant(span[i]);
            if (c == '0' && i + 1 < span.Length && char.ToUpperInvariant(span[i + 1]) == 'X')
            {
                i += 2;
                continue;
            }
            if (c is ' ' or '\t' or '\n' or '\r' or ',')
            {
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }

        var digits = sb.ToString();
        if (digits.Length == 0)
        {
            value = Array.Empty<byte>();
            error = "输入为空";
            return false;
        }
        if (digits.Length % 2 != 0)
        {
            value = Array.Empty<byte>();
            error = $"HEX 输入为奇数位({digits.Length} 个字符),无法按字节对齐";
            return false;
        }
        foreach (var c in digits)
        {
            if (!Uri.IsHexDigit(c))
            {
                value = Array.Empty<byte>();
                error = $"非法 HEX 字符:{c}";
                return false;
            }
        }

        value = new byte[digits.Length / 2];
        for (var j = 0; j < value.Length; j++)
        {
            value[j] = byte.Parse(digits.AsSpan(j * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        error = null;
        return true;
    }

    public static string ToHexString(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) return string.Empty;

        var sb = new System.Text.StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
