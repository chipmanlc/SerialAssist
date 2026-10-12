using NUnit.Framework;

namespace SerialAssist.Core.Tests;

/// <summary>
/// RenderPolicy 规格测试(P5 渲染节流):洪流下单轮渲染有预算,积压超阈值时视图聚合跳尾
/// (跳过仅限视图,日志按序列号仍逐条完整)。UI 线程每个渲染周期只做有界工作量。
/// </summary>
[TestFixture]
public class RenderPolicyTests
{
    // ---------- 核心场景 ----------

    [Test]
    public void 积压为空_不渲染不跳过()
    {
        var plan = RenderPolicy.Plan(0);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Take, Is.EqualTo(0));
            Assert.That(plan.Skip, Is.EqualTo(0));
        });
    }

    [Test]
    public void 积压小于单轮预算_全部渲染不跳过()
    {
        var plan = RenderPolicy.Plan(500);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Take, Is.EqualTo(500));
            Assert.That(plan.Skip, Is.EqualTo(0));
        });
    }

    [Test]
    public void 积压超单轮预算但未到聚合阈值_本轮渲染预算量_剩余下轮补()
    {
        var plan = RenderPolicy.Plan(RenderPolicy.CoalesceThreshold);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Take, Is.EqualTo(RenderPolicy.MaxEntriesPerTick));
            Assert.That(plan.Skip, Is.EqualTo(0), "未超聚合阈值不丢视图数据,靠多轮追");
        });
    }

    [Test]
    public void 积压超聚合阈值_跳到尾部只渲染TailKeep()
    {
        var plan = RenderPolicy.Plan(RenderPolicy.CoalesceThreshold + 5000);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Take, Is.EqualTo(RenderPolicy.TailKeep));
            Assert.That(plan.Skip, Is.EqualTo(10_000), "Skip = backlog - TailKeep,尾部之前的全部聚合跳过");
        });
    }

    // ---------- 不变量 ----------

    [Test]
    public void 不变量_渲染加跳过_恰好消费全部积压()
    {
        for (var backlog = 0; backlog <= RenderPolicy.CoalesceThreshold + 8000; backlog += 137)
        {
            var plan = RenderPolicy.Plan(backlog);
            if (backlog > RenderPolicy.CoalesceThreshold)
            {
                Assert.That(plan.Take + plan.Skip, Is.EqualTo(backlog), $"backlog={backlog}");
            }
            else
            {
                Assert.That(plan.Take, Is.LessThanOrEqualTo(backlog), $"backlog={backlog}");
            }
        }
    }

    [Test]
    public void 不变量_常量关系_TailKeep不大于单轮预算_预算不大于聚合阈值()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderPolicy.TailKeep, Is.LessThanOrEqualTo(RenderPolicy.MaxEntriesPerTick));
            Assert.That(RenderPolicy.MaxEntriesPerTick, Is.LessThan(RenderPolicy.CoalesceThreshold));
        });
    }

    // ---------- 边界 ----------

    [Test]
    public void 负数积压_按0处理()
    {
        var plan = RenderPolicy.Plan(-5);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Take, Is.EqualTo(0));
            Assert.That(plan.Skip, Is.EqualTo(0));
        });
    }
}
