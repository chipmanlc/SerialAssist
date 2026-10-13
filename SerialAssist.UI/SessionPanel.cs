using SerialAssist.Core;
using SerialAssist.Transport;
using System.Text;
using TextRenderer = SerialAssist.Core.TextRenderer;

namespace SerialAssist.UI;

/// <summary>
/// 单串口面板(P3):参数栏 + 接收区(悬停冻结/文本HEX即切/时间戳) + 发送区(文本HEX) + 计数。
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
    private bool _suppressLogDialog;
    private bool _suppressRadioConvert;

    private PortConfig? _config;
    private Button _openCloseButton = null!;
    private RichTextBox _receiveBox = null!;
    private TextBox _sendBox = null!;
    private RadioButton _recvTextRadio = null!;
    private RadioButton _recvHexRadio = null!;
    private RadioButton _sendTextRadio = null!;
    private RadioButton _sendHexRadio = null!;
    private CheckBox _timestampCheck = null!;
    private ComboBox _encodingCombo = null!;
    private CheckBox _logCheck = null!;
    private ComboBox _lineEndingCombo = null!;
    private Label _previewLabel = null!;
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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildReceiveArea());
        root.Controls.Add(BuildSendArea());
    }

    private Control BuildReceiveArea()
    {
        var group = new GroupBox { Text = "接收区", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
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
            UpdateSendPreview(); // 发送区与接收区共用编码,切换后预览同步重投影
        };
        var jumpLatestButton = new Button { Text = "跳到最新", Width = 76, Height = 24 };
        jumpLatestButton.Click += (_, _) =>
        {
            _receiveBox.SelectionStart = _receiveBox.TextLength;
            _receiveBox.ScrollToCaret();
        };
        var clearRecvButton = new Button { Text = "清除屏幕&&回滚", Width = 112, Height = 24 };
        clearRecvButton.Click += OnClearReceive;
        _logCheck = new CheckBox { Text = "记录日志", AutoSize = true };
        _logCheck.CheckedChanged += OnLogToggled;
        _statusLabel = new Label { Text = "未打开", AutoSize = true, ForeColor = Color.DimGray };

        toolbar.Controls.Add(_openCloseButton);
        toolbar.Controls.Add(_recvTextRadio);
        toolbar.Controls.Add(_recvHexRadio);
        toolbar.Controls.Add(_timestampCheck);
        toolbar.Controls.Add(new Label { Text = " 编码:", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        toolbar.Controls.Add(_encodingCombo);
        toolbar.Controls.Add(clearRecvButton);
        toolbar.Controls.Add(jumpLatestButton);
        toolbar.Controls.Add(_logCheck);
        toolbar.Controls.Add(_statusLabel);

        _receiveBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9.5f), DetectUrls = false };
        _receiveBox.MouseEnter += (_, _) =>
        {
            _frozen = true;
            UpdateStatus("悬停预览中", Color.DarkOrange);
        };
        _receiveBox.MouseLeave += (_, _) =>
        {
            _frozen = false;
            // 离开悬停只清掉"悬停预览中",不回显"已打开 COMx"(页签已有端口名,常态提示是噪音)
            UpdateStatus(string.Empty, Color.DimGray);
            RenderNewEntries();
        };

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_receiveBox, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildSendArea()
    {
        var group = new GroupBox { Text = "发送区", Dock = DockStyle.Fill, Height = 110 };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _sendBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, Font = new Font("Consolas", 9.5f) };
        // 回车即发送(Shift+Enter 才换行),与 SSCOM 等工位工具习惯一致
        _sendBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true; // 不插入换行、不响系统提示音
                OnSendClicked(this, e);
            }
        };
        _sendTextRadio = new RadioButton { Text = "文本", Checked = true, AutoSize = true };
        _sendHexRadio = new RadioButton { Text = "HEX", AutoSize = true };
        // 切换发送模式 = 同一份数据换显示形态:点文本显示原文,点 HEX 显示 hex 字节串
        // 只响应"被选中"的那次:取消选中的 CheckedChanged 也会触发,不挡会把刚转的内容转回去
        _sendTextRadio.CheckedChanged += (_, _) => { UpdateLineEndingEnabled(); if (_sendTextRadio.Checked) OnSendModeChanged(toText: true); };
        _sendHexRadio.CheckedChanged += (_, _) => { UpdateLineEndingEnabled(); if (_sendHexRadio.Checked) OnSendModeChanged(toText: false); };
        // 行尾可选:默认 CR——多数 CLI 设备(如 admin\r 登入)只认回车,\n 会被当空输入
        // 显示项与索引映射统一走 NewlineOptions,禁止 (NewlineMode)SelectedIndex 强转(顺序错位事故)
        // 仅文本模式生效(HEX 协议帧字节必须精确),HEX 模式下拉置灰防误导
        _lineEndingCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _lineEndingCombo.Items.AddRange(NewlineOptions.DisplayNames);
        _lineEndingCombo.SelectedIndex = 0;
        var sendButton = new Button { Text = "发送", Width = 64, Height = 30 };
        sendButton.Click += OnSendClicked;
        var clearSendButton = new Button { Text = "清空", Width = 64, Height = 30 };
        clearSendButton.Click += (_, _) => _sendBox.Clear(); // TextChanged 自动带动预览行刷新
        var fileButton = new Button { Text = "发送文件", Width = 84, Height = 30 };
        fileButton.Click += OnSendFileClicked;
        var frameButton = new Button { Text = "数据帧", Width = 76, Height = 30 };
        frameButton.Click += (_, _) => FramesRequested?.Invoke();

        var modeRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        modeRow.Controls.Add(_sendTextRadio);
        modeRow.Controls.Add(_sendHexRadio);
        modeRow.Controls.Add(new Label { Text = " 行尾:", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        modeRow.Controls.Add(_lineEndingCombo);

        var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        side.Controls.Add(modeRow);
        // 按钮横排放一行:竖排第三个按钮曾被面板高度裁掉不可见(Height 110 只够两行)
        var buttonRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        buttonRow.Controls.Add(sendButton);
        buttonRow.Controls.Add(clearSendButton);
        buttonRow.Controls.Add(fileButton);
        buttonRow.Controls.Add(frameButton);
        side.Controls.Add(buttonRow);

        // 双视图实时预览:打字/切模式/换行尾/换编码都即时刷新(文本模式显示HEX,HEX模式显示解码文本)
        _previewLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            Height = 20,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Text = "预览:",
        };
        _sendBox.TextChanged += (_, _) => UpdateSendPreview();
        _lineEndingCombo.SelectedIndexChanged += (_, _) => UpdateSendPreview();

        layout.Controls.Add(_sendBox, 0, 0);
        layout.Controls.Add(side, 1, 0);
        layout.Controls.Add(_previewLabel, 0, 1);
        layout.SetColumnSpan(_previewLabel, 2);
        group.Controls.Add(layout);
        UpdateSendPreview();
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
            return; // 状态栏保持 MouseEnter 写入的"悬停预览中",不再刷缓冲条数
        }

        // 回滚看历史提示:视图离底即提示"回滚预览中",拖回底部自动清除。
        // 每 tick 都检测(无新数据也要响应用户拖滚动条);悬停冻结时由"悬停预览中"接管。
        var atBottom = IsViewAtBottom();
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

    /// <summary>清空接收框但保留 Ctrl+滚轮缩放:RichEdit 在文本被清空时会把 ZoomFactor 重置回 100%。</summary>
    private void ClearReceiveBoxPreserveZoom()
    {
        var zoom = _receiveBox.ZoomFactor;
        _receiveBox.Clear();
        _receiveBox.ZoomFactor = zoom;
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

    private void OnSendClicked(object? sender, EventArgs e)
    {
        var mode = _sendHexRadio.Checked ? SendMode.Hex : SendMode.Text;
        var newline = NewlineOptions.FromIndex(_lineEndingCombo.SelectedIndex);
        var payload = SendFormatter.Format(mode, _sendBox.Text, GetEncoding(), newline);
        if (!payload.Ok)
        {
            UpdateStatus($"发送失败:{payload.Error}", Color.Firebrick);
            return;
        }
        if (_session.Send(payload.Bytes))
        {
            UpdateStatus($"已发送 {payload.Bytes.Length} 字节", Color.DimGray);
        }
        else
        {
            UpdateStatus($"发送失败:{_session.LastSendError ?? "串口未打开"}", Color.Firebrick);
        }
    }

    /// <summary>
    /// 切换发送模式:同一份数据换显示形态(文本↔HEX 互转发送框内容,行尾不掺入,发送时才附加)。
    /// HEX 转不动时留在 HEX 模式并报错,内容绝不丢。
    /// </summary>
    private void OnSendModeChanged(bool toText)
    {
        UpdateSendPreview();
        if (_suppressRadioConvert || string.IsNullOrWhiteSpace(_sendBox.Text))
        {
            return;
        }
        if (toText)
        {
            if (!HexCodec.TryParse(_sendBox.Text, out var bytes, out var error))
            {
                UpdateStatus($"转换失败:{error}(内容未变)", Color.Firebrick);
                _suppressRadioConvert = true;
                _sendHexRadio.Checked = true; // 弹回 HEX 模式
                _suppressRadioConvert = false;
                return;
            }
            _sendBox.Text = GetEncoding().GetString(bytes);
        }
        else
        {
            _sendBox.Text = HexCodec.ToHexString(GetEncoding().GetBytes(_sendBox.Text));
        }
    }

    /// <summary>行尾只作用于文本模式:HEX 模式置灰,提示用户该参数不参与字节构造。</summary>
    private void UpdateLineEndingEnabled() => _lineEndingCombo.Enabled = _sendTextRadio.Checked;

    /// <summary>发送框双视图预览:文本模式显示将发送字节的 HEX,HEX 模式显示解码文本。</summary>
    private void UpdateSendPreview()
    {
        if (_previewLabel is null)
        {
            return; // BuildUi 构建顺序中编码下拉先于预览行创建,防 null
        }
        const int MaxPreviewChars = 512; // 超长截断,防大文本拖慢 Label
        var mode = _sendHexRadio.Checked ? SendMode.Hex : SendMode.Text;
        var preview = SendPreview.Render(mode, _sendBox.Text, GetEncoding(), NewlineOptions.FromIndex(_lineEndingCombo.SelectedIndex));
        if (preview.Length > MaxPreviewChars)
        {
            preview = preview[..MaxPreviewChars] + " …";
        }
        _previewLabel.Text = preview.Length == 0 ? "预览:" : $"预览: {preview}";
    }

    /// <summary>
    /// 选定文件发送(2026-10-12 用户新增,2026-10-13 修订):文本模式 = 文件原始字节 + 行尾下拉所选结尾
    /// (选"无"即纯原始字节);HEX 模式 = 纯原始字节(字节精确,行尾不掺入,与发送框规则一致)。
    /// 状态栏显示开头字节的 HEX 预览供核对。
    /// </summary>
    private void OnSendFileClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Title = "选择要发送的文件" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            UpdateStatus($"读取文件失败:{ex.Message}", Color.Firebrick);
            return;
        }
        if (bytes.Length == 0)
        {
            UpdateStatus("文件为空,未发送", Color.DarkOrange);
            return;
        }

        // 文本模式下按行尾下拉追加结尾;HEX 模式直发原始字节
        var fileBytes = bytes;
        var endingNote = string.Empty;
        if (_sendTextRadio.Checked)
        {
            var ending = NewlineOptions.FromIndex(_lineEndingCombo.SelectedIndex) switch
            {
                NewlineMode.CR => new byte[] { 0x0D },
                NewlineMode.LF => new byte[] { 0x0A },
                NewlineMode.CRLF => new byte[] { 0x0D, 0x0A },
                _ => Array.Empty<byte>(),
            };
            if (ending.Length > 0)
            {
                fileBytes = [.. bytes, .. ending];
                endingNote = ",含行尾";
            }
        }

        if (_session.Send(fileBytes))
        {
            // HEX 预览头 16 字节,核对"发出去的到底是什么"
            var head = HexCodec.ToHexString(fileBytes[..Math.Min(16, fileBytes.Length)]);
            var ellipsis = fileBytes.Length > 16 ? " …" : string.Empty;
            UpdateStatus($"已发送 {Path.GetFileName(dialog.FileName)}({fileBytes.Length} 字节{endingNote}):{head}{ellipsis}", Color.DimGray);
        }
        else
        {
            UpdateStatus($"文件发送失败:{_session.LastSendError ?? "串口未打开"}", Color.Firebrick);
        }
    }

    // ---------- 杂项 ----------

    /// <summary>帧面板的发送通道:任意线程可调(循环发送在后台任务里跑),内部无 UI 依赖。</summary>
    public bool SendFrameBytes(byte[] bytes) => _session.Send(bytes);

    /// <summary>当前接收/发送区编码:数据帧文本模式按它转字节,与手动发送保持一致。</summary>
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
