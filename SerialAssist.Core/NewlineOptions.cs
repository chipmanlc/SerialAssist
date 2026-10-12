namespace SerialAssist.Core;

/// <summary>
/// 行尾下拉框的显示项与 NewlineMode 的映射契约(2026-10-12 真机事故固证):
/// 下拉框显示顺序是 [CR, LF, CRLF, 无],而枚举数值是 None=0/CR=1/LF=2/CRLF=3,
/// 直接 (NewlineMode)SelectedIndex 强转会整体错一位——界面选 CR 实际发 None。
/// 显示项与映射收拢到同一处,顺序调整时编译器和测试同时兜底。
/// </summary>
public static class NewlineOptions
{
    /// <summary>下拉框显示文本,索引即下拉框索引,必须与 <see cref="FromIndex"/> 保持对齐。</summary>
    public static readonly string[] DisplayNames = ["CR (\\r)", "LF (\\n)", "CRLF (\\r\\n)", "无"];

    /// <summary>下拉框索引 → 行尾模式;越界(只可能是 bug)回退默认 CR。</summary>
    public static NewlineMode FromIndex(int index) => index switch
    {
        0 => NewlineMode.CR,
        1 => NewlineMode.LF,
        2 => NewlineMode.CRLF,
        3 => NewlineMode.None,
        _ => SendFormatter.DefaultNewline,
    };
}
