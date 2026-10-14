using SerialAssist.Core;
using System.Text;

namespace SerialAssist.UI;

/// <summary>
/// 报文帧面板(P6/S6):帧列表(启用勾选/增删改/上下移)+ 单发(选中帧)/连发(启用帧)/循环(可停)。
/// 发送走 FrameRunner 后台任务,发送字节经注入的 sender(会话发送+TX 计数),不占 UI 线程。
/// </summary>
internal sealed class FramePanel : UserControl
{
    private readonly FrameStore _store;
    private readonly Func<byte[], bool> _sender;
    // 编码用委托实时取:会话面板改编码后,帧文本模式必须跟着变,快照会发错字节
    private readonly Func<Encoding> _encoding;
    // 目标名实时取(与共享发送区同一目标):null = 还没点选任何串口窗口
    private readonly Func<string?> _targetName;
    private readonly ListView _list;
    private readonly Button _loopButton;
    private readonly Label _statusLabel;
    private readonly Button _newButton = null!;
    private readonly Button _editButton = null!;
    private readonly Button _deleteButton = null!;
    private readonly Button _upButton = null!;
    private readonly Button _downButton = null!;
    private readonly Button _sendSelButton = null!;
    private readonly Button _sendEnabledButton = null!;
    private CancellationTokenSource? _loopCts;
    private bool _suppressCheckSync;

    public FramePanel(FrameStore store, Func<byte[], bool> sender, Func<Encoding>? encoding = null, Func<string?>? targetName = null)
    {
        _store = store;
        _sender = sender;
        _encoding = encoding ?? (() => Encoding.GetEncoding("GBK"));
        _targetName = targetName ?? (() => null);
        // 字体按用户要求用 Times New Roman(西文衬线;中文字形系统自动回退,整体偏传统纸质感)
        Font = new Font("Times New Roman", 10f);

        _newButton = new Button { Text = "新增", Width = 56 };
        _editButton = new Button { Text = "编辑", Width = 56 };
        _deleteButton = new Button { Text = "删除", Width = 56 };
        _upButton = new Button { Text = "上移", Width = 56 };
        _downButton = new Button { Text = "下移", Width = 56 };
        _newButton.Click += (_, _) => EditFrame(-1);
        _editButton.Click += (_, _) => EditFrame(FirstSelected());
        _deleteButton.Click += (_, _) => DeleteSelected();
        _upButton.Click += (_, _) => MoveSelected(-1);
        _downButton.Click += (_, _) => MoveSelected(1);

        _sendSelButton = new Button { Text = "单发", Width = 60 };
        _sendEnabledButton = new Button { Text = "连发", Width = 60 };
        _loopButton = new Button { Text = "循环", Width = 60 };
        _sendSelButton.Click += (_, _) => SendExplicit();
        _sendEnabledButton.Click += (_, _) => SendEnabled();
        _loopButton.Click += (_, _) => ToggleLoop();
        _statusLabel = new Label { Text = "就绪", AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left };

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, WrapContents = false, AutoSize = true, Padding = new Padding(4) };
        foreach (var b in new[] { _newButton, _editButton, _deleteButton, _upButton, _downButton })
        {
            toolbar.Controls.Add(b);
        }

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, WrapContents = false, AutoSize = true, Padding = new Padding(4) };
        bottom.Controls.Add(_sendSelButton);
        bottom.Controls.Add(_sendEnabledButton);
        bottom.Controls.Add(_loopButton);
        bottom.Controls.Add(_statusLabel);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            CheckBoxes = true,
            HideSelection = false,
        };
        _list.Columns.Add("名称", 120);
        _list.Columns.Add("HEX 内容", 210);
        _list.Columns.Add("校验", 95);
        _list.Columns.Add("延时ms", 60);
        _list.ItemChecked += OnItemChecked;
        _list.DoubleClick += (_, _) => EditFrame(FirstSelected());

        Controls.Add(_list);
        Controls.Add(bottom);
        Controls.Add(toolbar);

        RebuildList();
    }

    // ---------- 列表 ----------

    private int FirstSelected() => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

    private void RebuildList()
    {
        _suppressCheckSync = true;
        var selected = _list.SelectedIndices.Cast<int>().ToHashSet();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var frame in _store.Frames)
        {
            var item = new ListViewItem(frame.Name) { Checked = frame.Enabled };
            item.SubItems.Add(HexCodec.ToHexString(frame.BuildBytes()));
            item.SubItems.Add(ChecksumOptions.NameOf(frame.Checksum));
            item.SubItems.Add(frame.DelayAfterMs.ToString());
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        foreach (var i in selected)
        {
            if (i < _list.Items.Count)
            {
                _list.Items[i].Selected = true;
            }
        }
        _suppressCheckSync = false;
    }

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_suppressCheckSync || e.Item.Index >= _store.Frames.Count)
        {
            return;
        }
        var frame = _store.Frames[e.Item.Index];
        if (frame.Enabled != e.Item.Checked)
        {
            // DataFrame 不可变,启用开关 = 原字段重建一份(含原始输入回显字段)
            _store.Replace(e.Item.Index,
                new DataFrame(frame.Name, frame.Payload, frame.Checksum, frame.DelayAfterMs)
                {
                    Enabled = e.Item.Checked,
                    SourceInput = frame.SourceInput,
                    InputMode = frame.InputMode,
                });
        }
    }

    private void EditFrame(int index)
    {
        using var dialog = new FrameEditDialog(index >= 0 ? _store.Frames[index] : null, _encoding());
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
        {
            return;
        }
        if (index >= 0)
        {
            _store.Replace(index, dialog.Result);
        }
        else
        {
            _store.Add(dialog.Result);
        }
        RebuildList();
        SetStatus(index >= 0 ? "帧已更新" : "帧已新增", Color.DimGray);
    }

    private void DeleteSelected()
    {
        foreach (var index in _list.SelectedIndices.Cast<int>().OrderByDescending(i => i))
        {
            _store.RemoveAt(index);
        }
        RebuildList();
    }

    private void MoveSelected(int delta)
    {
        var index = FirstSelected();
        if (index < 0)
        {
            return;
        }
        var moved = delta < 0 ? _store.MoveUp(index) : _store.MoveDown(index);
        if (moved)
        {
            RebuildList();
        }
    }

    // ---------- 发送 ----------

    /// <summary>发送前三查:没点选任何串口窗口就明确报"先选目标",别让用户误判成串口没打开。</summary>
    private bool EnsureTarget()
    {
        if (_targetName() is not null)
        {
            return true;
        }
        SetStatus("先用鼠标点一下要发送的串口窗口(与发送区同一目标)", Color.DarkOrange);
        return false;
    }

    private void SetStatus(string text, Color color)
    {
        if (!_statusLabel.IsDisposed)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }
    }

    private void SetBusy(bool busy)
    {
        _sendSelButton.Enabled = _sendEnabledButton.Enabled = !busy;
        _newButton.Enabled = _editButton.Enabled = _deleteButton.Enabled = _upButton.Enabled = _downButton.Enabled = !busy;
    }

    /// <summary>
    /// 单发:勾选帧优先(与连发/循环同一套勾选语义,勾了就发勾的);
    /// 无勾选帧时退回蓝色高亮选中帧(旧行为)。状态栏明示本次按哪套发的。
    /// </summary>
    private async void SendExplicit()
    {
        if (!EnsureTarget())
        {
            return;
        }
        // 勾选优先:读列表勾选态(与 store.Enabled 经 ItemChecked 同步,两者一致)
        var checkedIdx = Enumerable.Range(0, _list.Items.Count).Where(i => _list.Items[i].Checked).ToList();
        IReadOnlyList<DataFrame> frames;
        string source;
        if (checkedIdx.Count > 0)
        {
            frames = checkedIdx.Select(i => _store.Frames[i]).ToList();
            source = "勾选";
        }
        else if (_list.SelectedIndices.Count > 0)
        {
            frames = _list.SelectedIndices.Cast<int>().Select(i => _store.Frames[i]).ToList();
            source = "选中";
        }
        else
        {
            SetStatus("先勾选要发送的帧(或蓝色选中后单发)", Color.DarkOrange);
            return;
        }

        SetBusy(true);
        try
        {
            var runner = new FrameRunner(_sender);
            var ok = true;
            foreach (var frame in frames)
            {
                if (!await runner.SendOnceAsync(frame))
                {
                    ok = false;
                    break;
                }
                if (frame.DelayAfterMs > 0)
                {
                    await Task.Delay(frame.DelayAfterMs);
                }
            }
            SetStatus(ok
                ? $"已发送 {frames.Count} 帧(按{source}) → {_targetName()}"
                : $"发送失败(按{source},串口未打开或被流控阻塞)", ok ? Color.DimGray : Color.Firebrick);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>连发:所有启用帧按顺序一遍(核心层自动跳过停用帧)。</summary>
    private async void SendEnabled()
    {
        if (!EnsureTarget())
        {
            return;
        }
        SetBusy(true);
        try
        {
            var runner = new FrameRunner(_sender);
            var ok = await runner.SendSelectedAsync(_store.Frames);
            SetStatus(ok ? $"连发完成 → {_targetName()}" : "连发中断(发送失败或串口未打开)", ok ? Color.DimGray : Color.Firebrick);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ToggleLoop()
    {
        if (_loopCts is not null)
        {
            _loopCts.Cancel(); // 循环任务自己收尾并复位按钮
            SetStatus("正在停止循环…", Color.DarkOrange);
            return;
        }
        if (!EnsureTarget())
        {
            return;
        }
        if (!_store.Frames.Any(f => f.Enabled))
        {
            SetStatus("没有启用帧,先勾选要循环发送的帧", Color.DarkOrange);
            return;
        }
        var cts = new CancellationTokenSource();
        _loopCts = cts;
        _loopButton.Text = "停止";
        SetBusy(true);
        SetStatus($"循环发送中 → {_targetName()}(以启动时的帧列表为准,改动需停止后重启循环)", Color.ForestGreen);
        var runner = new FrameRunner(_sender);
        // 快照必须在 UI 线程拍:FrameStore 是普通 List,线程池并发 ToArray 与 UI 改列表会竞态
        var snapshot = _store.Frames.ToArray();
        Task.Run(async () =>
        {
            try
            {
                await runner.LoopAsync(snapshot, cts.Token);
            }
            finally
            {
                if (!IsDisposed)
                {
                    BeginInvoke(() =>
                    {
                        _loopButton.Text = "循环";
                        SetBusy(false);
                        _loopCts?.Dispose();
                        _loopCts = null;
                        SetStatus("循环已停止", Color.DimGray);
                    });
                }
                cts.Dispose();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loopCts?.Cancel();
        }
        base.Dispose(disposing);
    }
}
