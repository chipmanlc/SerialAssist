namespace SerialAssist.Core;

/// <summary>
/// 帧列表数据层(P6/S6):≥10 条帧的增/删/改/上移/下移。
/// 所有索引操作越界安全(返回 false 不抛),UI 直接绑定无守卫负担。
/// </summary>
public sealed class FrameStore
{
    private readonly List<DataFrame> _frames = new();

    public IReadOnlyList<DataFrame> Frames => _frames;

    public void Add(DataFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames.Add(frame);
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _frames.Count)
        {
            return false;
        }
        _frames.RemoveAt(index);
        return true;
    }

    public bool MoveUp(int index)
    {
        if (index <= 0 || index >= _frames.Count)
        {
            return false;
        }
        (_frames[index - 1], _frames[index]) = (_frames[index], _frames[index - 1]);
        return true;
    }

    public bool MoveDown(int index)
    {
        if (index < 0 || index >= _frames.Count - 1)
        {
            return false;
        }
        (_frames[index], _frames[index + 1]) = (_frames[index + 1], _frames[index]);
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
        return true;
    }
}
