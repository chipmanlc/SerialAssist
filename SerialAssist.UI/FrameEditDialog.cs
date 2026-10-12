using SerialAssist.Core;
using System.Text;

namespace SerialAssist.UI;

/// <summary>校验模式下拉框的显示项与枚举映射(教训:绝不依赖"顺序恰好一致"的隐式映射)。</summary>
internal static class ChecksumOptions
{
    public static readonly (string Name, ChecksumMode Mode)[] Items =
    [
        ("无", ChecksumMode.None),
        ("Sum8 累加和", ChecksumMode.Sum8),
        ("CRC8", ChecksumMode.Crc8),
        ("CRC16 Modbus", ChecksumMode.Crc16Modbus),
    ];

    public static string NameOf(ChecksumMode mode) =>
        Items.FirstOrDefault(i => i.Mode == mode).Name ?? mode.ToString();

    public static ChecksumMode FromIndex(int index) =>
        index >= 0 && index < Items.Length ? Items[index].Mode : ChecksumMode.None;

    public static int IndexOf(ChecksumMode mode)
    {
        for (var i = 0; i < Items.Length; i++)
        {
            if (Items[i].Mode == mode)
            {
                return i;
            }
        }
        return 0;
    }
}

/// <summary>
/// 帧编辑对话框(P6,2026-10-12 扩展):名字 + payload + 校验附加 + 帧后延时。
/// payload 双模式输入:HEX(HexCodec 容忍规则)或文本(C 风格转义 \r \n \t \0 \\ \xHH,
/// 按会话编码转字节)。OK 时 Result 给出帧,SourceInput/InputMode 记录原始输入供回显。
/// </summary>
internal sealed class FrameEditDialog : Form
{
    private readonly TextBox _nameBox = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _textRadio = new() { Text = "文本", AutoSize = true };
    private readonly RadioButton _hexRadio = new() { Text = "HEX", Checked = true, AutoSize = true };
    private readonly TextBox _payloadBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 60, Font = new Font("Consolas", 9.5f) };
    private readonly ComboBox _checksumCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _delayBox = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 60_000, Value = 0 };
    private readonly Label _errorLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _okButton = new() { Text = "确定", DialogResult = DialogResult.OK, Width = 80 };
    private readonly Encoding _encoding;

    public DataFrame? Result { get; private set; }

    public FrameEditDialog(DataFrame? existing = null, Encoding? encoding = null)
    {
        _encoding = encoding ?? Encoding.GetEncoding("GBK");

        Text = existing is null ? "新增报文帧" : "编辑报文帧";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 260);

        _checksumCombo.Items.AddRange(ChecksumOptions.Items.Select(i => i.Name).ToArray());
        _checksumCombo.SelectedIndex = existing is null ? 0 : ChecksumOptions.IndexOf(existing.Checksum);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        void AddRow(int row, string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            grid.Controls.Add(control, 1, row);
        }

        AddRow(0, "名称:", _nameBox);

        var modeRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        modeRow.Controls.Add(_textRadio);
        modeRow.Controls.Add(_hexRadio);
        // 只响应"被选中"的那次:取消选中的 CheckedChanged 也会触发
        _textRadio.CheckedChanged += (_, _) => { if (_textRadio.Checked) OnInputModeChanged(); };
        _hexRadio.CheckedChanged += (_, _) => { if (_hexRadio.Checked) OnInputModeChanged(); };
        AddRow(1, "输入模式:", modeRow);
        AddRow(2, "内容:", _payloadBox);
        AddRow(3, "校验:", _checksumCombo);
        AddRow(4, "帧后延时:", _delayBox);
        grid.Controls.Add(_errorLabel, 1, 5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            AutoSize = true,
        };
        buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 });
        buttons.Controls.Add(_okButton);

        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = _okButton;
        CancelButton = (Button)buttons.Controls[0]!;

        if (existing is not null)
        {
            _nameBox.Text = existing.Name;
            // 回显原始输入,不做 HEX↔文本反推(有损):文本帧回原文,HEX 帧回十六进制
            _textRadio.Checked = existing.InputMode == FrameInputMode.Text;
            _hexRadio.Checked = existing.InputMode != FrameInputMode.Text;
            _payloadBox.Text = existing.InputMode == FrameInputMode.Text
                ? existing.SourceInput
                : HexCodec.ToHexString(existing.Payload);
            _delayBox.Value = Math.Min(existing.DelayAfterMs, _delayBox.Maximum);
        }

        _payloadBox.TextChanged += (_, _) => ValidatePayload();
        _okButton.Click += OnOkClicked;
        ValidatePayload();
        _nameBox.SelectAll();
        _nameBox.Focus();
    }

    /// <summary>切换输入模式时内容留在原地(原样是另一种模式的合法输入),立即重校验。</summary>
    private void OnInputModeChanged()
    {
        UpdateHint();
        ValidatePayload();
    }

    /// <summary>当前输入模式下解析 payload。</summary>
    private bool TryGetPayload(out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;
        return _hexRadio.Checked
            ? HexCodec.TryParse(_payloadBox.Text, out bytes, out error)
            : TextEscapeCodec.TryParse(_payloadBox.Text, _encoding, out bytes, out error);
    }

    private void ValidatePayload()
    {
        var empty = string.IsNullOrWhiteSpace(_payloadBox.Text);
        // 先赋默认值:empty 时 && 短路不进 TryGetPayload,out 参数保持默认
        byte[] bytes = Array.Empty<byte>();
        string? error = null;
        var ok = !empty && TryGetPayload(out bytes, out error);
        if (empty)
        {
            UpdateHint();
            _okButton.Enabled = false;
        }
        else if (ok)
        {
            _errorLabel.Text = $"共 {bytes.Length} 字节:{TruncateHex(bytes)}";
            _errorLabel.ForeColor = Color.ForestGreen;
            _okButton.Enabled = true;
        }
        else
        {
            _errorLabel.Text = error;
            _errorLabel.ForeColor = Color.Firebrick;
            _okButton.Enabled = false;
        }
    }

    private void UpdateHint()
    {
        _errorLabel.Text = _hexRadio.Checked
            ? "输入 HEX 字节,可用空格/逗号分隔"
            : "输入文本,支持转义:\\r \\n \\t \\0 \\\\ \\xHH";
        _errorLabel.ForeColor = Color.DimGray;
    }

    private static string TruncateHex(byte[] bytes)
    {
        var hex = HexCodec.ToHexString(bytes);
        return hex.Length <= 120 ? hex : hex[..120] + " …";
    }

    private void OnOkClicked(object? sender, EventArgs e)
    {
        // OK 按钮在 payload 非法时禁用,这里再守一道(防键盘 AcceptButton 绕过)
        if (!TryGetPayload(out var bytes, out _) || bytes.Length == 0)
        {
            return;
        }
        var name = string.IsNullOrWhiteSpace(_nameBox.Text) ? $"帧 {DateTime.Now:HHmmss}" : _nameBox.Text.Trim();
        Result = new DataFrame(name, bytes, ChecksumOptions.FromIndex(_checksumCombo.SelectedIndex), (int)_delayBox.Value)
        {
            SourceInput = _payloadBox.Text,
            InputMode = _hexRadio.Checked ? FrameInputMode.Hex : FrameInputMode.Text,
        };
    }
}
