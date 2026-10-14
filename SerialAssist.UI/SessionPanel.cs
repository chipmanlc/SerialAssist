using SerialAssist.Core;
using SerialAssist.Transport;
using System.Text;
using TextRenderer = SerialAssist.Core.TextRenderer;

namespace SerialAssist.UI;

/// <summary>
/// 单串口面板:只含接收区(悬停冻结/文本HEX即切/时间戳/日志)。
/// 发送区已上移为窗口底部共享发送区(SharedSendPanel),勾选目标串口发送。
/// 线程边界:session.DataReceived 在线程池线程 → pipeline.Push(线程安全);
/// UI 只在 30ms 定时器里读快照渲染,永不直接跨线程碰控件。
/// </summary>
internal sealed class SessionPanel : UserControl
{
    private const int MaxViewChars = 4_000_000; // 接收区视图上限,超出清视图防 RichTextBox 拖垮 UI

    private readonly SerialPortTransport _transport = new();
    private readonly SerialSession _session;
    private readonly ReceivePipeline _pipeline = new();
    private TextRenderer _textRenderer = new(Encoding.GetEncoding("GBK"));
    private readonly HexRenderer _hexRenderer = new();
    private readonly System.Windows.Forms.Timer _renderTimer;
    private long _renderedSeq;
    private long _loggedSeq;
    private const int LogBudgetPerTick = 4_000; // 单轮最多写日志行数,防洪流时写盘拖死 UI 线程(欠账下轮追)
    private SessionLogger? _logger;
    private bool _frozen;
    private bool _scrollBackHint;
    private float _savedZoom = 1f;
    private bool _zoomRestorePending;
    private bool _wasMinimized;
    private bool _lastAtBottom = true;
    private float _lastZoom = 1f;
    private bool _suppressLogDialog;

    private PortConfig? _config;
    private Button _openCloseButton = null!;
    private RichTextBox _receiveBox = null!;
    private RadioButton _recvTextRadio = null!;
    private RadioButton _recvHexRadio = null!;
    private CheckBox _timestampCheck = null!;
    private ComboBox _encodingCombo = null!;
    private CheckBox _logCheck = null!;
    private Label _logPathLabel = null!;
    private Label _statusLabel = null!;

    /// <summary>状态变化时上抛建议标题(如 "COM5 · 已打开"),供停靠面板标题用。</summary>
    public event Action<string>? TitleChanged;

    /// <summary>用户请求打开本会话的报文帧面板(MainForm 据此停靠 FramePanel)。</summary>
    public event Action? FramesRequested;

    /// <summary>本会话的报文帧列表(P6),帧面板与会话共享同一份。</summary>
    public FrameStore Frames { get; } = new();

    /// <summary>面板当前端口名(供帧面板标题等展示)。</summary>
    public string? DisplayPortName => _config?.PortName ?? _session.Config?.PortName;

    public SessionPanel()
    {
        _session = new SerialSession(_transport);
        _session.StateChanged += OnStateChanged;
        _session.DataReceived += OnDataReceived;

        _renderTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _renderTimer.Tick += (_, _) => RenderNewEntries();

        BuildUi();
        _renderTimer.Start();
    }

    // ---------- UI 构建(手写布局) ----------

    private void BuildUi()
    {
        // 发送区已移至窗口底部共享(SharedSendPanel),面板内只剩接收区
        Controls.Add(BuildReceiveArea());
    }

    private Control BuildReceiveArea()
    {
        var group = new GroupBox { Text = "接收区", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // WrapContents=true:窗口窄(平铺/网格)时按钮自动折行,状态栏永远完整可见
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _openCloseButton = new Button { Text = "打开", Width = 64, Height = 24 };
        _openCloseButton.Click += OnOpenCloseClicked;
        _recvTextRadio = new RadioButton { Text = "文本", Checked = true, AutoSize = true };
        _recvHexRadio = new RadioButton { Text = "HEX", AutoSize = true };
        _recvTextRadio.CheckedChanged += (_, _) => ReRenderAll();
        _recvHexRadio.CheckedChanged += (_, _) => ReRenderAll();
        _timestampCheck = new CheckBox { Text = "时间戳", AutoSize = true };
        _timestampCheck.CheckedChanged += (_, _) => ReRenderAll();
        // 编码选择:GBK 兼容国内工位设备中文回显(SSCOM 的 ANSI 习惯);切换后从字节源重投影,无需重收
        _encodingCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        _encodingCombo.Items.AddRange(["GBK", "UTF-8", "ASCII"]);
        _encodingCombo.SelectedIndex = 0;
        _encodingCombo.SelectedIndexChanged += (_, _) =>
        {
            _textRenderer = new TextRenderer(GetEncoding());
            ReRenderAll();
        };
        var jumpLatestButton = new Button { Text = "跳到最新", Width = 76, Height = 24 };
        jumpLatestButton.Click += (_, _) =>
        {
            _receiveBox.SelectionStart = _receiveBox.TextLength;
            _receiveBox.ScrollToCaret();
        };
        var clearRecvButton = new Button { Text = "清除屏幕&&回滚", Width = 112, Height = 24 };
        clearRecvButton.Click += OnClearReceive;
        // 数据帧按钮从原发送区迁来(发送区已共享化上移)
        var frameButton = new Button { Text = "数据帧", Width = 76, Height = 24 };
        frameButton.Click += (_, _) => FramesRequested?.Invoke();
        _logCheck = new CheckBox { Text = "记录日志", AutoSize = true };
        _logCheck.CheckedChanged += OnLogToggled;
        // 日志文件名常驻显示:状态栏是临时消息(会被悬停/滚动提示顶掉),路径在这里固定可见
        _logPathLabel = new Label { Text = string.Empty, AutoSize = true, ForeColor = Color.ForestGreen };
        _statusLabel = new Label { Text = "未打开", AutoSize = true, ForeColor = Color.DimGray };

        toolbar.Controls.Add(_openCloseButton);
        toolbar.Controls.Add(_recvTextRadio);
        toolbar.Controls.Add(_recvHexRadio);
        toolbar.Controls.Add(_timestampCheck);
        toolbar.Controls.Add(new Label { Text = " 编码:", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        toolbar.Controls.Add(_encodingCombo);
        toolbar.Controls.Add(clearRecvButton);
        toolbar.Controls.Add(jumpLatestButton);
        toolbar.Controls.Add(frameButton);
        toolbar.Controls.Add(_logCheck);
        toolbar.Controls.Add(_logPathLabel);
        toolbar.Controls.Add(_statusLabel);

        _receiveBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9.5f), DetectUrls = false };
        _receiveBox.MouseEnter += (_, _) =>
        {
            _frozen = true;
            // 已在回滚态(视图离底)不切"悬停预览中":用户正翻历史,悬停提示是打扰。
            // 仍置 _frozen 冻结渲染,让状态栏保持"回滚预览中"不被其他消息刷掉
            if (IsViewAtBottom())
            {
                UpdateStatus("悬停预览中", Color.DarkOrange);
            }
        };
        _receiveBox.MouseLeave += (_, _) =>
        {
            _frozen = false;
            // 复位回滚提示标志:悬停期间"悬停预览中"顶掉了"回滚预览中",
            // 但 _scrollBackHint 还挂着"已显示",不复位的话离开悬停后即使仍在回滚态也不再提示
            _scrollBackHint = false;
            // 离开悬停只清掉"悬停预览中",不回显"已打开 COMx"(页签已有端口名,常态提示是噪音)
            UpdateStatus(string.Empty, Color.DimGray);
            RenderNewEntries();
        };

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_receiveBox, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    // ---------- 打开/关闭 ----------

    private void OnOpenCloseClicked(object? sender, EventArgs e)
    {
        if (_session.State == SessionState.Open)
        {
            _session.Close();
            return;
        }
        if (_config is null)
        {
            UpdateStatus("未配置端口", Color.Firebrick);
            return;
        }
        _session.Open(_config);
    }

    /// <summary>会话状态的(状态栏文字, 颜色)。状态栏的"常规态"描述只从这里出,别处写必须能被它覆盖。</summary>
    private (string Text, Color Color) DescribeState(SessionState state)
    {
        var portName = DisplayPortName;
        return state switch
        {
            SessionState.Open => ($"已打开 {portName}", Color.ForestGreen),
            SessionState.Opening => ("正在打开…", Color.DimGray),
            SessionState.Error => ($"错误:{_session.LastError}", Color.Firebrick),
            _ => ("未打开", Color.DimGray),
        };
    }

    private void OnStateChanged(SessionState state)
    {
        // FatalError 来自线程池线程,必须 marshal 回 UI 线程;
        // 面板可能恰在关闭(句柄销毁中),全程防 Dispose 竞态,异常吞掉即可(状态更新已无意义)
        try
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(() =>
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _openCloseButton.Text = state == SessionState.Open ? "关闭" : "打开";
                var (text, color) = DescribeState(state);
                UpdateStatus(text, color);
                var portName = DisplayPortName;
                TitleChanged?.Invoke(state switch
                {
                    SessionState.Open => $"{portName}", // 打开态不带后缀(用户要求,页签只显示端口名)
                    SessionState.Error => $"{portName} · 错误",
                    SessionState.Opening => $"{portName} · 打开中",
                    _ => $"{portName}(未打开)",
                });
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // BeginInvoke 前一瞬句柄被销毁:放弃本次状态更新
        }
    }

    // ---------- 数据流 ----------

    private void OnDataReceived(byte[] bytes)
    {
        _pipeline.Push(bytes);
    }

    /// <summary>用户是否停在视图底部(决定渲染后是否跟随滚动;用户向上翻页时绝不抢位置)。</summary>
    private bool IsViewAtBottom()
    {
        if (_receiveBox.TextLength == 0)
        {
            return true;
        }
        var endPos = _receiveBox.GetPositionFromCharIndex(_receiveBox.TextLength - 1);
        return endPos.Y <= _receiveBox.ClientSize.Height - 1;
    }

    /// <summary>
    /// 30ms 批量消费:按累计序列号定位新数据(背压丢最旧/清空都不会错位或假死),
    /// 日志与视图各自独立推进;渲染单轮有预算(RenderPolicy),洪流时视图聚合跳尾。
    /// 只有用户本在底部才跟随滚动。
    /// </summary>
    private void RenderNewEntries()
    {
        var entries = _pipeline.Snapshot();
        var firstSeq = _pipeline.FirstSequence;

        // Clear 检测:消费序号超过管线累计数,说明管线被清过,归零重来
        if (_loggedSeq > _pipeline.TotalEntries)
        {
            _loggedSeq = _pipeline.TotalEntries;
        }
        if (_renderedSeq > _pipeline.TotalEntries)
        {
            _renderedSeq = _pipeline.TotalEntries;
        }

        // 日志落盘独立于渲染:冻结/翻历史期间也记;单轮限量,欠账下轮追
        if (_logger is null)
        {
            _loggedSeq = _pipeline.TotalEntries; // 未开日志时直接追平,之后开启也不回写历史
        }
        else
        {
            var logFrom = Math.Max(_loggedSeq, firstSeq);
            var logAvail = (int)(entries.Count - (logFrom - firstSeq));
            if (logAvail > 0)
            {
                var logTake = Math.Min(logAvail, LogBudgetPerTick);
                for (var i = 0; i < logTake; i++)
                {
                    var entry = entries[(int)(logFrom - firstSeq) + i];
                    _logger.Log(entry.Timestamp, entry.Bytes);
                }
                _loggedSeq = logFrom + logTake;
            }
        }

        if (_frozen)
        {
            // 悬停中也检测滚动:悬停时用滚轮上翻历史应切"回滚预览中",滚回底部再切回"悬停预览中"。
            // 渲染仍然冻结(返回),只是状态栏跟随滚动位置
            var hoverAtBottom = IsViewAtBottom();
            if (!hoverAtBottom)
            {
                if (!_scrollBackHint)
                {
                    _scrollBackHint = true;
                    UpdateStatus("回滚预览中", Color.DarkOrange);
                }
            }
            else if (_scrollBackHint)
            {
                _scrollBackHint = false;
                UpdateStatus("悬停预览中", Color.DarkOrange);
            }
            return; // 渲染冻结,不刷缓冲条数
        }

        // 回滚看历史提示:视图离底即提示"回滚预览中",拖回底部自动清除。
        // 每 tick 都检测(无新数据也要响应用户拖滚动条);悬停冻结时由"悬停预览中"接管。
        //
        // 最小化坑:窗口最小化时客户区塌为 0,IsViewAtBottom 必误判"离底",
        // 导致跟随中断;恢复后 ScrollToCaret 不会自动补,状态栏就永久卡在"回滚预览中"。
        // 处理:最小化期间沿用最小化前的跟随状态(_lastAtBottom);
        // 恢复后的第一拍,若最小化前本在底部,强制回底部对齐。
        // 最小化信号必须取顶层主窗体:停靠面板外层是 DockContent 子窗体,
        // 主窗体最小化时子窗体自己的 WindowState 仍是 Normal,查 FindForm() 永远查不到。
        var topForm = FindForm()?.TopLevelControl as Form;
        var minimized = _receiveBox.ClientSize.Height <= 0
            || topForm is { WindowState: FormWindowState.Minimized };
        var justRestored = _wasMinimized && !minimized;
        _wasMinimized = minimized;

        bool atBottom;
        if (justRestored)
        {
            atBottom = IsViewAtBottom();
            if (_lastAtBottom && !atBottom)
            {
                _receiveBox.SelectionStart = _receiveBox.TextLength;
                _receiveBox.ScrollToCaret();
                atBottom = true;
            }
        }
        else
        {
            atBottom = minimized || IsViewAtBottom();
            // Ctrl+滚轮缩放会挪动视图锚点:放大后末行被挤出视野,IsViewAtBottom 误判成"离底回滚"。
            // 缩放前本在跟随底部的话,缩放后强制回底部对齐,不报回滚
            var zoomNow = _receiveBox.ZoomFactor;
            var zoomed = Math.Abs(zoomNow - _lastZoom) > 0.001f;
            _lastZoom = zoomNow;
            if (zoomed && !minimized && _lastAtBottom && !atBottom)
            {
                _receiveBox.SelectionStart = _receiveBox.TextLength;
                _receiveBox.ScrollToCaret();
                atBottom = true;
            }
        }

        if (!minimized)
        {
            _lastAtBottom = atBottom;
        }

        if (!atBottom)
        {
            if (!_scrollBackHint)
            {
                _scrollBackHint = true;
                UpdateStatus("回滚预览中", Color.DarkOrange);
            }
        }
        else if (_scrollBackHint)
        {
            _scrollBackHint = false;
            UpdateStatus(string.Empty, Color.DimGray);
        }

        var viewFrom = Math.Max(_renderedSeq, firstSeq);
        var backlog = (int)(entries.Count - (viewFrom - firstSeq));
        var plan = RenderPolicy.Plan(backlog);
        _renderedSeq = viewFrom + plan.Skip + plan.Take;

        if (plan.Take == 0)
        {
            return;
        }

        // 关键:上面已取过"是否在底部"(atBottom)。只有"本来就在底部"才跟随滚动;
        // 用户拖回滚条看历史时,新数据静默追加,绝不把视图拽回底部。

        var showHex = _recvHexRadio.Checked;
        var showTs = _timestampCheck.Checked;
        var startIdx = (int)(viewFrom - firstSeq) + plan.Skip;
        var sb = new StringBuilder();
        for (var i = startIdx; i < startIdx + plan.Take; i++)
        {
            var entry = entries[i];
            if (showTs)
            {
                sb.Append('[').Append(entry.Timestamp.ToString("HH:mm:ss.fff")).Append("] ");
            }
            sb.Append(showHex ? _hexRenderer.Render(entry.Bytes) : _textRenderer.Render(entry.Bytes));
            sb.Append('\n');
        }

        if (_receiveBox.TextLength + sb.Length > MaxViewChars)
        {
            ClearReceiveBoxPreserveZoom();
            UpdateStatus("视图过长已清屏(数据仍在管线中)", Color.DarkOrange);
        }
        _receiveBox.AppendText(sb.ToString());
        if (_zoomRestorePending)
        {
            // 清屏后第一次追加数据:此刻 RichEdit 已把缩放打回 100%,还原用户之前的缩放
            _zoomRestorePending = false;
            if (Math.Abs(_receiveBox.ZoomFactor - _savedZoom) > 0.01f)
            {
                _receiveBox.ZoomFactor = _savedZoom;
            }
        }
        if (plan.Skip > 0)
        {
            UpdateStatus($"数据量过大,视图跳过 {plan.Skip} 条(接收日志仍完整)", Color.DarkOrange);
        }
        if (atBottom)
        {
            _receiveBox.SelectionStart = _receiveBox.TextLength;
            _receiveBox.ScrollToCaret();
        }
    }

    private void ReRenderAll()
    {
        if (_frozen)
        {
            return;
        }
        ClearReceiveBoxPreserveZoom();
        _renderedSeq = _pipeline.FirstSequence; // 从管线最旧开始重投影(单轮预算限量,多轮追完)
        RenderNewEntries();
    }

    /// <summary>
    /// 清空接收框但保留 Ctrl+滚轮缩放。RichEdit 的缩放重置有两次:Clear() 清空时一次,
    /// 空到非空的第一次 AppendText 又一次——当场还原无效,只能记下缩放值,
    /// 标记挂起,等下一拍追加数据后在 RenderNewEntries 里还原。
    /// </summary>
    private void ClearReceiveBoxPreserveZoom()
    {
        _savedZoom = _receiveBox.ZoomFactor;
        _zoomRestorePending = true;
        _receiveBox.Clear();
    }

    private void OnClearReceive(object? sender, EventArgs e)
    {
        _pipeline.Clear();
        ClearReceiveBoxPreserveZoom();
        _renderedSeq = 0;
        _loggedSeq = 0;
    }

    /// <summary>新建面板时锁定端口配置(参数只存在于 + 号对话框,面板内不展示)。</summary>
    public void PreselectConfig(PortConfig config)
    {
        _config = config;
        TitleChanged?.Invoke($"{config.PortName}(未打开)");
    }

    /// <summary>按创建时的配置直接打开串口(MainForm 在面板停靠后调用)。</summary>
    public void StartSession()
    {
        if (_config is not null)
        {
            _session.Open(_config);
        }
    }

    /// <summary>直接开始记录日志(新建对话框里已选好路径),不再弹对话框。</summary>
    public void StartLogging(string path)
    {
        _logger?.Dispose();
        _logger = new SessionLogger(path, append: true);
        _loggedSeq = _pipeline.TotalEntries; // 新日志从现在开始,不回写历史
        _suppressLogDialog = true;
        _logCheck.Checked = true;
        _suppressLogDialog = false;
        _logPathLabel.Text = $"日志:{Path.GetFileName(path)}";
        UpdateStatus($"记录日志:{path}", Color.ForestGreen);
    }

    private void OnLogToggled(object? sender, EventArgs e)
    {
        if (_logCheck.Checked)
        {
            if (_logger is not null || _suppressLogDialog)
            {
                return; // StartLogging 已建好 logger,或正在程序化勾选
            }
            using var dialog = new SaveFileDialog
            {
                Title = "选择接收日志保存位置",
                Filter = "日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                DefaultExt = "log",
                FileName = $"serial-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                _logCheck.Checked = false;
                return;
            }
            StartLogging(dialog.FileName);
        }
        else
        {
            _logger?.Dispose();
            _logger = null;
            _logPathLabel.Text = string.Empty;
            UpdateStatus("日志已停止", Color.DimGray);
        }
    }

    /// <summary>当前编码(GBK 为国内工位设备默认;失败回退 ASCII)。</summary>
    private Encoding GetEncoding()
    {
        try
        {
            return _encodingCombo.SelectedIndex switch
            {
                1 => Encoding.UTF8,
                2 => Encoding.ASCII,
                _ => Encoding.GetEncoding("GBK"),
            };
        }
        catch (ArgumentException)
        {
            return Encoding.ASCII; // CodePages 提供器未注册时兜底
        }
    }

    // ---------- 杂项 ----------

    /// <summary>帧面板的发送通道:任意线程可调(循环发送在后台任务里跑),内部无 UI 依赖。</summary>
    public bool SendFrameBytes(byte[] bytes) => _session.Send(bytes);

    /// <summary>共享发送区通道:UI 线程调用,失败原因见 LastSendError(状态栏提示由共享发送区统一报)。</summary>
    public bool TrySend(byte[] bytes) => _session.Send(bytes);

    /// <summary>共享发送区勾选框显示名。</summary>
    public string TargetName => DisplayPortName ?? "串口(未命名)";

    /// <summary>当前接收区编码:数据帧文本模式按它转字节;共享发送区文本模式也按各口自己的编码转。</summary>
    public Encoding TextEncoding => GetEncoding();

    private void UpdateStatus(string text, Color color)
    {
        if (_statusLabel.InvokeRequired)
        {
            BeginInvoke(() => { _statusLabel.Text = text; _statusLabel.ForeColor = color; });
            return;
        }
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop();
            _renderTimer.Dispose();
            _session.Dispose();
            _transport.Dispose();
            _logger?.Dispose();
        }
        base.Dispose(disposing);
    }
}
