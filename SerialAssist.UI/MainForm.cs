using SerialAssist.Core;
using System.Text;
using WeifenLuo.WinFormsUI.Docking;

namespace SerialAssist.UI;

/// <summary>P4 主窗体:DockPanelSuite 停靠宿主,多串口面板可并排/浮动/切分。</summary>
internal sealed class MainForm : Form
{
    private readonly DockPanel _dockPanel;
    private readonly SharedSendPanel _sharedSend;
    private readonly Dictionary<SessionPanel, DockContent> _sessionContents = new();
    private readonly List<SessionPanel> _sessionOrder = new(); // 创建顺序,平铺排版按它来(Dictionary 顺序不保证)
    private readonly FrameStore _frames; // 全软件一个帧列表,随主窗体存活
    private readonly string _framesPath;
    private Form? _frameForm; // 数据帧独立窗口(不进停靠体系,避免和串口窗口抢地方)
    private int _panelCount;

    public MainForm()
    {
        Text = "LuckyC SerialAssist";
        Icon = TryLoadAppIcon();
        Size = new Size(1240, 840);
        MinimumSize = new Size(900, 620);
        // 工具窗口优先放副屏,主屏留给用户工作;没有副屏时仍在主屏居中。
        StartPosition = FormStartPosition.Manual;
        var targetScreen = Screen.AllScreens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen!;
        var work = targetScreen.WorkingArea;
        Location = new Point(
            work.Left + Math.Max(0, (work.Width - Width) / 2),
            work.Top + Math.Max(0, (work.Height - Height) / 2));
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.UiFont();
        DoubleBuffered = true;

        _framesPath = Path.Combine(AppPaths.DataDir, "frames.json");
        _frames = new FrameStore();
        foreach (var frame in FramePersistence.Load(_framesPath))
        {
            _frames.Add(frame);
        }
        _frames.Changed += (_, _) => FramePersistence.Save(_framesPath, _frames.Frames);
        FormClosed += (_, _) => FramePersistence.Save(_framesPath, _frames.Frames);

        var header = BuildHeader();

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Padding = new Padding(14, 12, 14, 0) };
        _dockPanel = new DockPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        _dockPanel.ActiveContentChanged += (_, _) => RefreshSendTarget();
        _emptyHint = BuildEmptyState();
        body.Controls.Add(_dockPanel);
        body.Controls.Add(_emptyHint);
        _emptyHint.BringToFront(); // 空态只盖住工作区,不遮顶部和发送区

        _sharedSend = new SharedSendPanel { Dock = DockStyle.Fill };
        _sharedSend.FramesRequested += () => OpenFramePanel(); // 数据帧入口在发送区按钮行
        var sendHost = new Panel { Dock = DockStyle.Bottom, Height = 244, BackColor = UiTheme.Bg, Padding = new Padding(14, 6, 14, 12) };
        sendHost.Controls.Add(_sharedSend);

        Controls.Add(body);
        Controls.Add(sendHost);
        Controls.Add(header);

        UiTheme.StyleForm(this);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 74,
            BackColor = UiTheme.BgElevated,
            Padding = new Padding(20, 0, 16, 0),
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.BorderSoft);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brand = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 16, 0, 0),
            Margin = new Padding(0),
        };
        var logo = new PictureBox
        {
            Size = new Size(40, 40),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 12, 0),
        };
        try
        {
            logo.Image = Icon?.ToBitmap();
        }
        catch
        {
            logo.Visible = false;
        }
        var titles = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        titles.Controls.Add(new Label
        {
            Text = "LuckyC SerialAssist",
            AutoSize = true,
            Font = UiTheme.UiFont(13f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        }, 0, 0);
        titles.Controls.Add(new Label
        {
            Text = "SERIAL ASSISTANT",
            AutoSize = true,
            Font = UiTheme.UiFont(7.5f, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            Margin = new Padding(1, 2, 0, 0),
        }, 0, 1);
        brand.Controls.Add(logo);
        brand.Controls.Add(titles);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 17, 0, 0),
            Margin = new Padding(0),
        };
        var addButton = new ThemedButton
        {
            Text = "＋  新建串口",
            Kind = ThemedButtonKind.Primary,
            Width = 136,
            Height = 38,
            Margin = new Padding(8, 0, 0, 0),
        };
        addButton.Click += (_, _) => OpenSessionPanel();
        var tileButton = new ThemedButton
        {
            Text = "▦  平铺窗口",
            Kind = ThemedButtonKind.Secondary,
            Width = 116,
            Height = 38,
            Margin = new Padding(8, 0, 0, 0),
        };
        tileButton.Click += (_, _) => TileSessionWindows();
        var helpButton = new ThemedButton
        {
            Text = "?  操作说明",
            Kind = ThemedButtonKind.Ghost,
            Width = 112,
            Height = 38,
            Margin = new Padding(8, 0, 0, 0),
        };
        helpButton.Click += (_, _) =>
        {
            using var dialog = new HelpDialog();
            dialog.ShowDialog(this);
        };
        actions.Controls.Add(addButton);
        actions.Controls.Add(tileButton);
        actions.Controls.Add(helpButton);

        layout.Controls.Add(brand, 0, 0);
        layout.Controls.Add(actions, 1, 0);
        header.Controls.Add(layout);
        return header;
    }

    private Control BuildEmptyState()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        host.Paint += (_, e) =>
        {
            using var dot = new SolidBrush(Color.FromArgb(24, 33, 44));
            for (var x = 18; x < host.Width; x += 26)
            {
                for (var y = 18; y < host.Height; y += 26)
                {
                    e.Graphics.FillEllipse(dot, x, y, 2, 2);
                }
            }
        };

        var card = new ThemedPanel
        {
            Size = new Size(540, 262),
            FillColor = UiTheme.Surface,
            BorderColor = UiTheme.Border,
            OuterColor = UiTheme.Bg,
            CornerRadius = 16,
            Padding = new Padding(30),
        };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };
        var eyebrow = new Label
        {
            Text = "●  READY FOR A NEW SESSION",
            AutoSize = true,
            Font = UiTheme.UiFont(7.5f, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 14),
        };
        var title = new Label
        {
            Text = "准备开始一次串口会话",
            AutoSize = true,
            Font = UiTheme.UiFont(17f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
        };
        var body = new Label
        {
            Text = "配置端口、波特率与编码，打开连接后即可实时收发数据。\n所有串口会话共享底部发送区，点选窗口即可切换目标。",
            AutoSize = true,
            MaximumSize = new Size(465, 0),
            Font = UiTheme.UiFont(10f),
            ForeColor = UiTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 22),
        };
        var startButton = new ThemedButton
        {
            Text = "＋  新建串口连接",
            Kind = ThemedButtonKind.Primary,
            Width = 154,
            Height = 38,
            Margin = new Padding(0),
        };
        startButton.Click += (_, _) => OpenSessionPanel();
        content.Controls.Add(eyebrow);
        content.Controls.Add(title);
        content.Controls.Add(body);
        content.Controls.Add(startButton);
        card.Controls.Add(content);

        void CenterCard()
        {
            card.Left = Math.Max(0, (host.ClientSize.Width - card.Width) / 2);
            card.Top = Math.Max(0, (host.ClientSize.Height - card.Height) / 2);
        }
        host.Resize += (_, _) => CenterCard();
        host.Controls.Add(card);
        CenterCard();
        return host;
    }

    private readonly Control _emptyHint = null!;

    /// <summary>从 exe 提取应用图标作为窗体/任务栏图标;提取失败退回默认图标,不影响启动。</summary>
    private static Icon TryLoadAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    /// <summary>当前点选的会话:活动内容是串口面板则直接用;是某个会话的数据帧面板则归属其会话。
    /// 必须校验会话仍在册:FormClosed 时 DPS 可能还没切走 ActiveContent,否则会选中正在销毁的面板。</summary>
    private SessionPanel? ActiveSession
    {
        get
        {
            if (_dockPanel.ActiveContent is not DockContent content || content.IsDisposed)
            {
                return null;
            }
            if (content.Controls.Count > 0 && content.Controls[0] is SessionPanel session)
            {
                return _sessionContents.ContainsKey(session) ? session : null;
            }
            return null; // 数据帧面板是全局的,点它不算切换目标,保持上一个串口
        }
    }

    private SessionPanel? _sendTargetSession; // 共享发送区当前目标,只在点到串口窗口时更新

    /// <summary>活动窗口变化后刷新共享发送区的目标串口(编码用委托实时取)。
    /// 活动内容为空不清目标:点击共享发送区本身时 DPS 会把 ActiveContent 置空,清了目标就丢。</summary>
    private void RefreshSendTarget()
    {
        var session = ActiveSession;
        if (session is null)
        {
            return;
        }
        SetSendTarget(session);
    }

    private void SetSendTarget(SessionPanel session)
    {
        _sendTargetSession = session;
        _sharedSend.SetTarget(new SendTarget(session.TargetName, session.TrySend, () => session.TextEncoding));
    }

    /// <summary>
    /// 垂直平铺(2026-10-14 用户拍板为两列网格):2 窗左右各半;3 窗上二下一;4 窗上二下二;
    /// 5 窗上二下三;6 窗上二中二下二……以此类推(第 i 窗,0 基,落第 i%2 列)。
    /// 拆分递推:先把第 2 窗摆右侧各占 1/2;之后第 i 窗摆到本列上一窗(i-2)底部,
    /// 占该列 1/(已有行数+1)——恰好让同列各窗等高。
    /// </summary>
    private void TileSessionWindows()
    {
        var contents = _sessionOrder
            .Select(p => _sessionContents[p])
            .Where(c => !c.IsDisposed && c.DockState == DockState.Document)
            .ToList();
        if (contents.Count == 0)
        {
            return;
        }
        if (contents.Count >= 2)
        {
            contents[1].Show(contents[0].Pane!, DockAlignment.Right, 0.5);
        }
        for (var i = 2; i < contents.Count; i++)
        {
            var rowsInColumn = i / 2; // 该列在此之前已有的窗数
            contents[i].Show(contents[i - 2].Pane!, DockAlignment.Bottom, 1.0 / (rowsInColumn + 1));
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _dockPanel.Theme = new VS2012DarkTheme();
        // 启动工作区留空,不预开面板——串口面板只通过 + 号创建
    }

    /// <summary>新建串口面板。showDialog=true 时弹对话框配齐参数与日志;false 直接开默认面板(启动场景)。</summary>
    private void OpenSessionPanel(bool showDialog = true)
    {
        PortConfig? config = null;
        string? logPath = null;
        if (showDialog)
        {
            using var dialog = new NewSessionDialog();
            // 注意:必须 dialog.ShowDialog(this)——之前误写成 ShowDialog(dialog)
            // 变成了把 MainForm 自己当模态框显示,触发"已经可见的窗体不能显示为模式对话框"
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            logPath = dialog.LogPath;
            config = dialog.Config;
        }

        var panel = new SessionPanel();
        _emptyHint.Hide();
        var content = new DockContent
        {
            Text = $"串口 {++_panelCount}",
            CloseButton = true,
            CloseButtonVisible = true,
            BackColor = UiTheme.Bg,
        };
        panel.TitleChanged += title => content.Text = title;
        content.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
        _sessionContents[panel] = content;
        _sessionOrder.Add(panel);
        content.Show(_dockPanel, DockState.Document);
        content.FormClosed += (_, _) =>
        {
            _sessionContents.Remove(panel);
            _sessionOrder.Remove(panel);
            if (_sendTargetSession == panel)
            {
                // 关掉的正是发送目标:清掉,别留一个指向已销毁面板的悬空目标
                _sendTargetSession = null;
                _sharedSend.SetTarget(null);
            }
            // 不能同步刷:此刻 ActiveContent 可能还指着正在关闭的内容,会又把将死面板选成目标;
            // 等 DPS 完成关闭、ActiveContent 落定后再刷
            BeginInvoke(RefreshSendTarget);
            panel.Dispose();
            if (_dockPanel.Contents.Count == 0)
            {
                _emptyHint.Show(); // 全部面板关闭后恢复空态提示
                _emptyHint.BringToFront();
            }
        };

        if (config is not null)
        {
            panel.PreselectConfig(config);
            panel.StartSession(); // 参数只在对话框配,面板创建即开串口
        }
        if (!string.IsNullOrEmpty(logPath))
        {
            panel.StartLogging(logPath);
        }
        // 新面板创建后直接设为发送目标:不依赖 ActiveContentChanged 的时序
        SetSendTarget(panel);
        // 新开串口自动纳入网格(DPS 默认把新内容塞进已有 Document pane 当页签,必须重排才成窗)
        TileSessionWindows();
    }

    /// <summary>
    /// 打开共享数据帧窗口(全软件一个,2026-10-14 与发送区统一):帧发往「当前点选的串口」,
    /// 与共享发送区同一目标;已开则激活。独立悬浮窗,不进停靠体系——停靠会跟串口窗口
    /// 共分/切换同一块区域,用户明确要求独立窗口。
    /// </summary>
    private void OpenFramePanel()
    {
        if (_frameForm is { IsDisposed: false })
        {
            _frameForm.Activate();
            return;
        }

        var framePanel = new FramePanel(_frames, bytes => _sendTargetSession?.TrySend(bytes) ?? false,
            () => _sendTargetSession?.TextEncoding ?? GbkFallback(),
            () => _sendTargetSession?.TargetName)
        { Dock = DockStyle.Fill };
        _frameForm = new Form
        {
            Text = "数据帧",
            Size = new Size(680, 560),
            MinimumSize = new Size(560, 420),
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, // 工具型小窗,不占任务栏
            MinimizeBox = false,
            BackColor = UiTheme.Bg,
            Font = UiTheme.UiFont(),
        };
        UiTheme.StyleForm(_frameForm);
        _frameForm.Controls.Add(framePanel);
        _frameForm.FormClosed += (_, _) => _frameForm = null;
        _frameForm.Show(this); // 非 modal、挂主窗体为 owner:主窗体最小化它跟着藏,关闭顺序也安全
    }

    private static Encoding GbkFallback()
    {
        try
        {
            return Encoding.GetEncoding("GBK");
        }
        catch (ArgumentException)
        {
            return Encoding.ASCII;
        }
    }
}