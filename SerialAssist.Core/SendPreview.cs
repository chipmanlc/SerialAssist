using System.Text;

namespace SerialAssist.Core;

/// <summary>
/// 发送框双视图实时预览(2026-10-12 用户新增):同一份数据在文本/HEX 间切换显示。
/// 复用 SendFormatter.Format 生成"将要实际发出的字节"(含行尾),保证预览即所得:
/// 文本模式 → 预览 HEX;HEX 模式 → 预览解码文本(控制字符转义,肉眼可辨行尾)。
/// 空输入(打字中)返回空串不报错;非法 HEX 返回错误提示。
/// </summary>
public static class SendPreview
{
    public static string Render(SendMode mode, string? input, Encoding? encoding = null, NewlineMode newline = SendFormatter.DefaultNewline)
    {
        // 打字中的空态是常态,预览静默为空;SendFormatter 会把它当错误,这里先行拦截
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var payload = SendFormatter.Format(mode, input, encoding, newline);
        if (!payload.Ok)
        {
            return $"⚠ {payload.Error}";
        }

        return mode == SendMode.Text
            ? HexCodec.ToHexString(payload.Bytes)
            : EscapeText(payload.Bytes, encoding);
    }

    /// <summary>字节按编码解码为文本,控制字符转义显示(\r \n \t),其余原样。</summary>
    private static string EscapeText(byte[] bytes, Encoding? encoding)
    {
        var text = (encoding ?? Encoding.ASCII).GetString(bytes);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\r':
                    sb.Append(@"\r");
                    break;
                case '\n':
                    sb.Append(@"\n");
                    break;
                case '\t':
                    sb.Append(@"\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
