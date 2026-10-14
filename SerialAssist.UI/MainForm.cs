using SerialAssist.Core;
using WeifenLuo.WinFormsUI.Docking;

namespace SerialAssist.UI;

/// <summary>P4 主窗体:DockPanelSuite 停靠宿主,多串口面板可并排/浮动/切分。</summary>
internal sealed class MainForm : Form
{
    private readonly DockPanel _dockPanel;
    private readonly SharedSendPanel _sharedSend;
    private readonly Dictionary<SessionPanel, DockContent> _sessionContents = new();
    private readonly List<SessionPanel> _sessionOrder = new(); // 创建顺序,平铺排版按它来(Dictionary 顺序不保证)
    private readonly Dictionary<SessionPanel, DockContent> _framePanels = new();
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

    /// <summary>当前点选的会话:活动内容是串口面板则直接用;是某个会话的数据帧面板则归属其会话。</summary>
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
                return session;
            }
            return _framePanels.FirstOrDefault(kv => kv.Value == content).Key;
        }
    }

    /// <summary>活动窗口变化后刷新共享发送区的目标串口(编码用委托实时取)。</summary>
    private void RefreshSendTarget()
    {
        var session = ActiveSession;
        _sharedSend.SetTarget(session is null
            ? null
            : new SendTarget(session.TargetName, session.TrySend, () => session.TextEncoding));
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
        panel.FramesRequested += () => OpenFramePanel(panel);
        content.FormClosed += (_, _) =>
        {
            _sessionContents.Remove(panel);
            _sessionOrder.Remove(panel);
            RefreshSendTarget();
            if (_framePanels.Remove(panel, out var frameContent))
            {
                frameContent.Close(); // 会话关了,它的帧面板跟着关
            }
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

    /// <summary>打开某会话的报文帧面板:停靠在该会话 pane 右侧;已开则激活,不重复开。</summary>
    private void OpenFramePanel(SessionPanel panel)
    {
        if (_framePanels.TryGetValue(panel, out var existing) && !existing.IsDisposed)
        {
            existing.Activate();
            return;
        }

        var framePanel = new FramePanel(panel.Frames, panel.SendFrameBytes, panel.TextEncoding);
        var content = new DockContent
        {
            Text = $"数据帧 {panel.DisplayPortName}",
            CloseButton = true,
            CloseButtonVisible = true,
        };
        content.Controls.Add(framePanel);
        framePanel.Dock = DockStyle.Fill;
        var sessionContent = _sessionContents[panel];
        content.Show(sessionContent.Pane!, DockAlignment.Right, 0.4);
        content.FormClosed += (_, _) =>
        {
            _framePanels.Remove(panel);
            framePanel.Dispose();
        };
        _framePanels[panel] = content;
    }
}
