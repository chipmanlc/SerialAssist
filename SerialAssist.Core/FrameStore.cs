namespace SerialAssist.Core;

/// <summary>
/// 帧列表数据层(P6/S6):≥10 条帧的增/删/改/上移/下移。
/// 所有索引操作越界安全(返回 false 不抛),UI 直接绑定无守卫负担。
/// </summary>
public sealed class FrameStore
{
    private readonly List<DataFrame> _frames = new();

    public IReadOnlyList<DataFrame> Frames => _frames;

    /// <summary>帧列表发生增删改或排序后触发,持久化层据此落盘。</summary>
    public event EventHandler? Changed;

    public void Add(DataFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames.Add(frame);
        NotifyChanged();
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _frames.Count)
        {
            return false;
        }
        _frames.RemoveAt(index);
        NotifyChanged();
        return true;
    }

    public bool MoveUp(int index)
    {
        if (index <= 0 || index >= _frames.Count)
        {
            return false;
        }
        (_frames[index - 1], _frames[index]) = (_frames[index], _frames[index - 1]);
        NotifyChanged();
        return true;
    }

    public bool MoveDown(int index)
    {
        if (index < 0 || index >= _frames.Count - 1)
        {
            return false;
        }
        (_frames[index], _frames[index + 1]) = (_frames[index + 1], _frames[index]);
        NotifyChanged();
        return true;
    }

    public bool Replace(int index, DataFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (index < 0 || index >= _frames.Count)
        {
            return false;
        }
        _frames[index] = frame;
        NotifyChanged();
        return true;
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
