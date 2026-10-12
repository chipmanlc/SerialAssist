namespace SerialAssist.Core;

/// <summary>一个渲染周期的消费计划:Take 条渲染,前面的 Skip 条聚合跳过(仅视图;日志按序列号仍逐条)。</summary>
public readonly record struct RenderPlan(int Take, int Skip);

/// <summary>
/// 渲染节流策略(P5/S4):保证 UI 线程每个渲染周期的工作量有界。
/// 积压小 → 全量或按单轮预算分轮追;积压超聚合阈值(洪流)→ 直接跳到尾部,
/// 中间内容视图层面放弃(接收日志按序列号仍逐条完整)。纯函数,无状态。
/// </summary>
public static class RenderPolicy
{
    /// <summary>单轮最多渲染条数:把 AppendText/字符串构建的每 tick 成本钉在常数级。</summary>
    public const int MaxEntriesPerTick = 2_000;

    /// <summary>积压超过此数视为洪流,视图聚合跳尾(继续逐条渲染只会越欠越多,UI 越来越卡)。</summary>
    public const int CoalesceThreshold = 6_000;

    /// <summary>洪流时视图保留的尾部条数。</summary>
    public const int TailKeep = 1_000;

    public static RenderPlan Plan(long backlog) => backlog switch
    {
        <= 0 => new RenderPlan(0, 0),
        > CoalesceThreshold => new RenderPlan(TailKeep, (int)(backlog - TailKeep)),
        _ => new RenderPlan((int)Math.Min(backlog, MaxEntriesPerTick), 0),
    };
}
