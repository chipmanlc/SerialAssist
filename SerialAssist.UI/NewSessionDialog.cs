using System.IO.Ports;
using SerialAssist.Core;

namespace SerialAssist.UI;

/// <summary>
/// 新建串口面板对话框:全部串口参数 + 是否顺带开日志,在此一次配齐。
/// OK 后读 Config / LogPath(LogPath 为 null = 不记日志)。
/// </summary>
internal sealed class NewSessionDialog : Form
{
    private readonly ThemedSelect _portCombo = new() { Width = 210 };
    private readonly TextBox _baudBox = new() { Width = 150, Text = "9600" };
    private readonly ThemedButton _baudMenuButton = new() { Text = "▾", Kind = ThemedButtonKind.Ghost, Width = 26, Height = 22 };
    private readonly ThemedSelect _dataBitsCombo = new() { Width = 110 };
    private readonly ThemedSelect _stopBitsCombo = new() { Width = 110 };
    private readonly ThemedSelect _parityCombo = new() { Width = 160 };
    private readonly ThemedSelect _handshakeCombo = new() { Width = 220 };
    private readonly SwitchCheckBox _logCheck = new() { Text = "打开后开始记录日志" };
    private readonly TextBox _pathBox = new() { Width = 270, Enabled = false };
    private readonly ThemedButton _browseButton = new() { Text = "浏览…", Width = 74, Height = 30, Enabled = false };
    private readonly Label _portHint = new() { Text = string.Empty, AutoSize = true, ForeColor = UiTheme.Warning, Margin = new Padding(0, 0, 0, 4) };

    public PortConfig Config => new()
    {
        PortName = _portCombo.SelectedItem as string ?? string.Empty,
        BaudRate = int.TryParse(_baudBox.Text.Trim(), out var baud) ? baud : -1,
        DataBits = _dataBitsCombo.SelectedIndex + 5,
        StopBits = _stopBitsCombo.SelectedIndex switch { 1 => StopBitsMode.OnePointFive, 2 => StopBitsMode.Two, _ => StopBitsMode.One },
        Parity = (ParityMode)Math.Max(0, _parityCombo.SelectedIndex),
        Handshake = (HandshakeMode)Math.Max(0, _handshakeCombo.SelectedIndex),
    };

    /// <summary>null = 不记日志;否则为完整日志路径。</summary>
    public string? LogPath => _logCheck.Checked ? _pathBox.Text : null;

    public NewSessionDialog()
    {
        Text = "新建串口会话";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(492, 552);
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.UiFont();
        UiTheme.StyleForm(this);

        _dataBitsCombo.AddRange("5", "6", "7", "8");
        _dataBitsCombo.SelectedIndex = 3;
        _stopBitsCombo.AddRange("1", "1.5", "2");
        _stopBitsCombo.SelectedIndex = 0;
        _parityCombo.AddRange("None", "Odd", "Even", "Mark", "Space");
        _parityCombo.SelectedIndex = 0;
        _handshakeCombo.AddRange("None", "RTS/CTS", "XOn/XOff", "RTS+XOnXOff");
        _handshakeCombo.SelectedIndex = 0;
        UiTheme.StyleTextBox(_baudBox);
        _baudBox.BorderStyle = BorderStyle.None;
        _baudBox.BackColor = UiTheme.SurfaceAlt;
        UiTheme.StyleTextBox(_pathBox);
        _baudMenuButton.Click += (_, _) => ShowBaudMenu();

        var ports = SerialPort.GetPortNames().Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        _portCombo.AddRange(ports);
        if (ports.Length > 0)
        {
            _portCombo.SelectedIndex = 0;
        }
        else
        {
            _portHint.Text = "未检测到可用串口。请连接设备后重新打开此窗口。";
        }

        // ---- 顶部标题 ----
        var header = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = UiTheme.BgElevated, Padding = new Padding(26, 20, 26, 12) };
        header.Controls.Add(new Label
        {
            Text = "新建串口会话",
            Dock = DockStyle.Top,
            Height = 30,
            Font = UiTheme.UiFont(16f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        header.Controls.Add(new Label
        {
            Text = "CONFIGURE A NEW SERIAL CONNECTION",
            Dock = DockStyle.Bottom,
            Height = 20,
            Font = UiTheme.UiFont(7.5f, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        // ---- 参数卡片 ----
        var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Padding = new Padding(20, 12, 20, 0) };
        var card = new ThemedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = UiTheme.Surface,
            BorderColor = UiTheme.BorderSoft,
            OuterColor = UiTheme.Bg,
            CornerRadius = 14,
            Padding = new Padding(18, 14, 18, 12),
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(int row, string label, Control control)
        {
            grid.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = UiTheme.UiFont(9f),
                ForeColor = UiTheme.TextMuted,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 7, 10, 7),
            }, 0, row);
            control.Anchor = AnchorStyles.Left;
            control.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(control, 1, row);
        }

        // 每行高度由控件决定
        for (var i = 0; i < 8; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(0, "串口", _portCombo);
        var baudWrap = new ThemedPanel
        {
            Width = 190,
            Height = 30,
            FillColor = UiTheme.SurfaceAlt,
            BorderColor = UiTheme.Border,
            OuterColor = UiTheme.Surface,
            CornerRadius = 7,
            Padding = new Padding(8, 4, 3, 4),
            Margin = new Padding(0),
        };
        _baudBox.Dock = DockStyle.Fill;
        _baudMenuButton.Dock = DockStyle.Right;
        _baudMenuButton.BackColor = UiTheme.SurfaceAlt;
        baudWrap.Controls.Add(_baudBox);
        baudWrap.Controls.Add(_baudMenuButton);
        AddRow(1, "波特率", baudWrap);
        AddRow(2, "数据位", _dataBitsCombo);
        AddRow(3, "停止位", _stopBitsCombo);
        AddRow(4, "校验位", _parityCombo);
        AddRow(5, "流控", _handshakeCombo);
        grid.Controls.Add(_portHint, 1, 6);
        grid.Controls.Add(_logCheck, 1, 7);

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Height = 30,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 0),
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Margin = new Padding(0, 2, 8, 2);
        _browseButton.Margin = new Padding(0);
        pathRow.Controls.Add(_pathBox, 0, 0);
        pathRow.Controls.Add(_browseButton, 1, 0);
        grid.Controls.Add(new Label
        {
            Text = "日志文件",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = UiTheme.UiFont(9f),
            ForeColor = UiTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 10, 0),
        }, 0, 8);
        grid.Controls.Add(pathRow, 1, 8);

        _logCheck.CheckedChanged += (_, _) =>
        {
            _pathBox.Enabled = _logCheck.Checked;
            _browseButton.Enabled = _logCheck.Checked;
            _pathBox.BackColor = _logCheck.Checked ? UiTheme.SurfaceAlt : UiTheme.Surface;
            _pathBox.ForeColor = _logCheck.Checked ? UiTheme.Text : UiTheme.TextMuted;
            if (_logCheck.Checked && _pathBox.Text.Length == 0)
            {
                _pathBox.Text = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"serial-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            }
        };
        _pathBox.BackColor = UiTheme.Surface;
        _pathBox.ForeColor = UiTheme.TextMuted;

        _browseButton.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = "选择日志保存位置",
                Filter = "日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                DefaultExt = "log",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dialog.FileName;
            }
        };

        card.Controls.Add(grid);
        bodyHost.Controls.Add(card);

        // ---- 底部按钮 ----
        var okButton = new ThemedButton
        {
            Text = "打开串口",
            Kind = ThemedButtonKind.Primary,
            Width = 108,
            Height = 38,
            DialogResult = DialogResult.OK,
            Enabled = ports.Length > 0,
            Margin = new Padding(8, 0, 0, 0),
        };
        var cancelButton = new ThemedButton
        {
            Text = "取消",
            Kind = ThemedButtonKind.Ghost,
            Width = 84,
            Height = 38,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 62,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = UiTheme.Bg,
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        Controls.Add(bodyHost);
        Controls.Add(buttons);
        Controls.Add(header);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
    private void ShowBaudMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = UiTheme.SurfaceAlt,
            ForeColor = UiTheme.Text,
            Font = UiTheme.UiFont(9f),
            Renderer = new ToolStripProfessionalRenderer(new BaudMenuColors()),
        };
        foreach (var baud in new[] { "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600", "3000000" })
        {
            var item = new ToolStripMenuItem(baud) { ForeColor = UiTheme.Text, BackColor = UiTheme.SurfaceAlt };
            item.Click += (_, _) => _baudBox.Text = baud;
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            try
            {
                _baudMenuButton.BeginInvoke(new Action(menu.Dispose));
            }
            catch (InvalidOperationException)
            {
                menu.Dispose();
            }
        };
        menu.Show(_baudMenuButton, new Point(0, _baudMenuButton.Height));
    }

    private sealed class BaudMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => UiTheme.SurfaceAlt;
        public override Color MenuItemSelected => UiTheme.SurfaceHover;
        public override Color MenuItemBorder => UiTheme.Border;
        public override Color MenuBorder => UiTheme.Border;
        public override Color ImageMarginGradientBegin => UiTheme.SurfaceAlt;
        public override Color ImageMarginGradientMiddle => UiTheme.SurfaceAlt;
        public override Color ImageMarginGradientEnd => UiTheme.SurfaceAlt;
    }}