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
    private readonly FrameStore _frames = new(); // 全软件一个帧列表,随主窗体存活
    private DockContent? _frameContent;
    private int _panelCount;

    public MainForm()
    {
        Text = "CHEN串口助手";
        Icon = TryLoadAppIcon();
        Size = new Size(1200, 820);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 560);

        // 工具栏:加号新建串口面板(参数与日志在弹出的对话框里一次配齐);退出走窗口关闭钮
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(20, 20) };
        var addButton = new ToolStripButton("+")
        {
            ToolTipText = "新建串口面板",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            DisplayStyle = ToolStripItemDisplayStyle.Text,
        };
        addButton.Click += (_, _) => OpenSessionPanel();
        toolbar.Items.Add(addButton);
        toolbar.Items.Add(new ToolStripSeparator());
        var tileButton = new ToolStripButton("垂直平铺")
        {
            ToolTipText = "把所有串口窗口左右等宽排开,一屏全见",
            DisplayStyle = ToolStripItemDisplayStyle.Text,
        };
        tileButton.Click += (_, _) => TileSessionWindows();
        toolbar.Items.Add(tileButton);
        toolbar.Items.Add(new ToolStripSeparator());
        var helpButton = new ToolStripButton("操作说明")
        {
            ToolTipText = "软件操作说明书",
            DisplayStyle = ToolStripItemDisplayStyle.Text,
        };
        helpButton.Click += (_, _) =>
        {
            using var dialog = new HelpDialog();
            dialog.ShowDialog(this); // 必须 this 为 owner,详见 OpenSessionPanel 内注释
        };
        toolbar.Items.Add(helpButton);

        _dockPanel = new DockPanel { Dock = DockStyle.Fill };
        _dockPanel.ActiveContentChanged += (_, _) => RefreshSendTarget();
        _sharedSend = new SharedSendPanel { Dock = DockStyle.Bottom, Height = 156 };
        _emptyHint = new Label
        {
            Text = "点击左上角 + 新建串口面板",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Font = new Font("Microsoft YaHei UI", 14f),
            BackColor = SystemColors.Control,
        };
        Controls.Add(_dockPanel);
        Controls.Add(_sharedSend);
        Controls.Add(toolbar);
        Controls.Add(_emptyHint);
        _emptyHint.BringToFront(); // 确保 hint 盖在 DockPanel 之上,首个面板打开后 Hide
    }

    private readonly Label _emptyHint = null!;

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
        _dockPanel.Theme = new VS2012LightTheme();
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
        };
        panel.TitleChanged += title => content.Text = title;
        content.Controls.Add(panel);
        panel.Dock = DockStyle.Fill;
        content.Show(_dockPanel, DockState.Document);
        _sessionContents[panel] = content;
        _sessionOrder.Add(panel);
        panel.FramesRequested += () => OpenFramePanel();
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
        // 刷新目标串口必须放在 PreselectConfig 之后:否则新面板还没有端口名,会显示"未命名"
        RefreshSendTarget();
        // 新开串口自动纳入网格(DPS 默认把新内容塞进已有 Document pane 当页签,必须重排才成窗)
        TileSessionWindows();
    }

    /// <summary>
    /// 打开共享数据帧面板(全软件一个,2026-10-14 与发送区统一):帧发往「当前点选的串口」,
    /// 与共享发送区同一目标;已开则激活。停靠在活动会话右侧,没有会话则作为文档窗。
    /// </summary>
    private void OpenFramePanel()
    {
        if (_frameContent is { IsDisposed: false })
        {
            _frameContent.Activate();
            return;
        }

        var framePanel = new FramePanel(_frames, bytes => _sendTargetSession?.TrySend(bytes) ?? false,
            () => _sendTargetSession?.TextEncoding ?? GbkFallback(),
            () => _sendTargetSession?.TargetName);
        var content = new DockContent
        {
            Text = "数据帧",
            CloseButton = true,
            CloseButtonVisible = true,
        };
        content.Controls.Add(framePanel);
        framePanel.Dock = DockStyle.Fill;
        var session = ActiveSession;
        if (session is not null && _sessionContents.TryGetValue(session, out var sessionContent) && sessionContent.Pane is not null)
        {
            content.Show(sessionContent.Pane, DockAlignment.Right, 0.4);
        }
        else
        {
            content.Show(_dockPanel, DockState.Document);
        }
        content.FormClosed += (_, _) =>
        {
            _frameContent = null;
            framePanel.Dispose();
        };
        _frameContent = content;
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
