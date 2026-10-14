using SerialAssist.Core;
using WeifenLuo.WinFormsUI.Docking;

namespace SerialAssist.UI;

/// <summary>P4 主窗体:DockPanelSuite 停靠宿主,多串口面板可并排/浮动/切分。</summary>
internal sealed class MainForm : Form
{
    private readonly DockPanel _dockPanel;
    private readonly Dictionary<SessionPanel, DockContent> _sessionContents = new();
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
        panel.FramesRequested += () => OpenFramePanel(panel);
        content.FormClosed += (_, _) =>
        {
            _sessionContents.Remove(panel);
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
