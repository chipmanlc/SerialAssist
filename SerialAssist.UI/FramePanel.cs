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
    private readonly ThemedButton _loopButton;
    private readonly Label _statusLabel;
    private readonly ThemedButton _newButton = null!;
    private readonly ThemedButton _editButton = null!;
    private readonly ThemedButton _deleteButton = null!;
    private readonly ThemedButton _upButton = null!;
    private readonly ThemedButton _downButton = null!;
    private readonly ThemedButton _sendSelButton = null!;
    private readonly ThemedButton _sendEnabledButton = null!;
    private CancellationTokenSource? _loopCts;
    private bool _suppressCheckSync;

    public FramePanel(FrameStore store, Func<byte[], bool> sender, Func<Encoding>? encoding = null, Func<string?>? targetName = null)
    {
        _store = store;
        _sender = sender;
        _encoding = encoding ?? (() => Encoding.GetEncoding("GBK"));
        _targetName = targetName ?? (() => null);
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.UiFont();

        var card = new ThemedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = UiTheme.Surface,
            BorderColor = UiTheme.BorderSoft,
            OuterColor = UiTheme.Bg,
            CornerRadius = 14,
            Padding = new Padding(14, 12, 14, 12),
        };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _newButton = new ThemedButton { Text = "＋ 新增", Kind = ThemedButtonKind.Primary, Width = 76, Height = 30, Margin = new Padding(0, 0, 7, 0) };
        _editButton = new ThemedButton { Text = "编辑", Kind = ThemedButtonKind.Secondary, Width = 60, Height = 30, Margin = new Padding(0, 0, 7, 0) };
        _deleteButton = new ThemedButton { Text = "删除", Kind = ThemedButtonKind.Danger, Width = 60, Height = 30, Margin = new Padding(0, 0, 7, 0) };
        _upButton = new ThemedButton { Text = "上移", Kind = ThemedButtonKind.Ghost, Width = 60, Height = 30, Margin = new Padding(0, 0, 7, 0) };
        _downButton = new ThemedButton { Text = "下移", Kind = ThemedButtonKind.Ghost, Width = 60, Height = 30, Margin = new Padding(0, 0, 0, 0) };
        _newButton.Click += (_, _) => EditFrame(-1);
        _editButton.Click += (_, _) => EditFrame(FirstSelected());
        _deleteButton.Click += (_, _) => DeleteSelected();
        _upButton.Click += (_, _) => MoveSelected(-1);
        _downButton.Click += (_, _) => MoveSelected(1);

        _sendSelButton = new ThemedButton { Text = "单发", Kind = ThemedButtonKind.Primary, Width = 72, Height = 32, Margin = new Padding(0, 0, 7, 0) };
        _sendEnabledButton = new ThemedButton { Text = "连发", Kind = ThemedButtonKind.Secondary, Width = 72, Height = 32, Margin = new Padding(0, 0, 7, 0) };
        _loopButton = new ThemedButton { Text = "循环", Kind = ThemedButtonKind.Secondary, Width = 72, Height = 32, Margin = new Padding(0, 0, 10, 0) };
        _sendSelButton.Click += (_, _) => SendExplicit();
        _sendEnabledButton.Click += (_, _) => SendEnabled();
        _loopButton.Click += (_, _) => ToggleLoop();
        _statusLabel = new Label
        {
            Text = "就绪",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 0, 0),
            Font = UiTheme.UiFont(8.5f),
        };

        // ---- 页头 ----
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleStack = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        titleStack.Controls.Add(new Label
        {
            Text = "数据帧",
            AutoSize = true,
            Font = UiTheme.UiFont(13f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        }, 0, 0);
        titleStack.Controls.Add(new Label
        {
            Text = "COMMAND FRAMES · 发往当前点选的串口",
            AutoSize = true,
            Font = UiTheme.UiFont(7.5f, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 3, 0, 0),
        }, 0, 1);
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        toolbar.Controls.Add(_newButton);
        toolbar.Controls.Add(_editButton);
        toolbar.Controls.Add(_deleteButton);
        toolbar.Controls.Add(_upButton);
        toolbar.Controls.Add(_downButton);
        header.Controls.Add(titleStack, 0, 0);
        header.Controls.Add(toolbar, 1, 0);

        // ---- 列表 ----
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            CheckBoxes = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.None,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.ReceiveBg,
            ForeColor = UiTheme.TextSecondary,
            Font = UiTheme.UiFont(9f),
            OwnerDraw = true,
            Margin = new Padding(0),
        };
        _list.Columns.Add("名称", 120);
        _list.Columns.Add("HEX 内容", 230);
        _list.Columns.Add("校验", 120);
        _list.Columns.Add("延时ms", 70);
        _list.ItemChecked += OnItemChecked;
        _list.DoubleClick += (_, _) => EditFrame(FirstSelected());
        _list.DrawItem += (_, e) => e.DrawDefault = true;
        _list.DrawSubItem += (_, e) => e.DrawDefault = true;
        var listHeader = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = UiTheme.SurfaceAlt };
        listHeader.Paint += (_, e) =>
        {
            using var bottomPen = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(bottomPen, 0, listHeader.Height - 1, listHeader.Width, listHeader.Height - 1);
            using var font = UiTheme.UiFont(8.5f, FontStyle.Bold);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            var headers = new[] { "名称", "HEX 内容", "校验", "延时ms" };
            var x = 0;
            for (var i = 0; i < headers.Length && i < _list.Columns.Count; i++)
            {
                var columnWidth = _list.Columns[i].Width;
                var left = x + (i == 0 ? 28 : 8);
                var width = Math.Max(0, columnWidth - (i == 0 ? 36 : 14));
                System.Windows.Forms.TextRenderer.DrawText(e.Graphics, headers[i], font,
                    new Rectangle(left, 0, width, listHeader.Height), UiTheme.TextMuted, flags);
                x += columnWidth;
            }
        };
        var listCard = new ThemedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = UiTheme.ReceiveBg,
            BorderColor = UiTheme.Border,
            OuterColor = UiTheme.Surface,
            CornerRadius = 10,
            Padding = new Padding(2, 2, 2, 2),
            Margin = new Padding(0, 0, 0, 10),
        };
        listCard.Controls.Add(_list);
        listCard.Controls.Add(listHeader);

        // ---- 底部发送操作 ----
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        bottom.Controls.Add(_sendSelButton);
        bottom.Controls.Add(_sendEnabledButton);
        bottom.Controls.Add(_loopButton);
        bottom.Controls.Add(_statusLabel);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(listCard, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        card.Controls.Add(root);
        Controls.Add(card);

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
            _statusLabel.ForeColor = UiTheme.StatusColor(color);
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
