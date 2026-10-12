using System.IO.Ports;
using SerialAssist.Core;

namespace SerialAssist.UI;

/// <summary>
/// 新建串口面板对话框:全部串口参数 + 是否顺带开日志,在此一次配齐。
/// OK 后读 Config / LogPath(LogPath 为 null = 不记日志)。
/// </summary>
internal sealed class NewSessionDialog : Form
{
    private readonly ComboBox _portCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _baudCombo = new() { Width = 120, Text = "9600" };
    private readonly ComboBox _dataBitsCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
    private readonly ComboBox _stopBitsCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly ComboBox _parityCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox _handshakeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _logCheck = new() { Text = "打开后开始记录日志", AutoSize = true };
    private readonly TextBox _pathBox = new() { Width = 260, Enabled = false };
    private readonly Button _browseButton = new() { Text = "浏览…", Enabled = false };

    public PortConfig Config => new()
    {
        PortName = _portCombo.SelectedItem as string ?? string.Empty,
        BaudRate = int.TryParse(_baudCombo.Text.Trim(), out var baud) ? baud : -1,
        DataBits = _dataBitsCombo.SelectedIndex + 5,
        StopBits = _stopBitsCombo.SelectedIndex switch { 1 => StopBitsMode.OnePointFive, 2 => StopBitsMode.Two, _ => StopBitsMode.One },
        Parity = (ParityMode)Math.Max(0, _parityCombo.SelectedIndex),
        Handshake = (HandshakeMode)Math.Max(0, _handshakeCombo.SelectedIndex),
    };

    /// <summary>null = 不记日志;否则为完整日志路径。</summary>
    public string? LogPath => _logCheck.Checked ? _pathBox.Text : null;

    public NewSessionDialog()
    {
        Text = "新建串口面板";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 320);

        _baudCombo.Items.AddRange(["9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600", "3000000"]);
        _baudCombo.Text = "9600";
        _dataBitsCombo.Items.AddRange(["5", "6", "7", "8"]);
        _dataBitsCombo.SelectedIndex = 3;
        _stopBitsCombo.Items.AddRange(["1", "1.5", "2"]);
        _stopBitsCombo.SelectedIndex = 0;
        _parityCombo.Items.AddRange(["None", "Odd", "Even", "Mark", "Space"]);
        _parityCombo.SelectedIndex = 0;
        _handshakeCombo.Items.AddRange(["None", "RTS/CTS", "XOn/XOff", "RTS+XOnXOff"]);
        _handshakeCombo.SelectedIndex = 0;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var ports = SerialPort.GetPortNames().Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        _portCombo.Items.AddRange(ports);
        if (ports.Length > 0)
        {
            _portCombo.SelectedIndex = 0;
        }

        layout.Controls.Add(new Label { Text = "串口:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_portCombo, 1, 0);
        layout.Controls.Add(new Label { Text = "波特率:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_baudCombo, 1, 1);
        layout.Controls.Add(new Label { Text = "数据位:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_dataBitsCombo, 1, 2);
        layout.Controls.Add(new Label { Text = "停止位:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_stopBitsCombo, 1, 3);
        layout.Controls.Add(new Label { Text = "校验位:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(_parityCombo, 1, 4);
        layout.Controls.Add(new Label { Text = "流控:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        layout.Controls.Add(_handshakeCombo, 1, 5);
        layout.Controls.Add(_logCheck, 1, 6);

        layout.Controls.Add(new Label { Text = "日志文件:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
        var pathRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        pathRow.Controls.Add(_pathBox);
        pathRow.Controls.Add(_browseButton);
        layout.Controls.Add(pathRow, 1, 7);

        _logCheck.CheckedChanged += (_, _) =>
        {
            _pathBox.Enabled = _logCheck.Checked;
            _browseButton.Enabled = _logCheck.Checked;
            if (_logCheck.Checked && _pathBox.Text.Length == 0)
            {
                _pathBox.Text = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"serial-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            }
        };

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

        var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 80 };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        okButton.Enabled = ports.Length > 0;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12, 0, 12, 8) };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}
