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
    private readonly ChipRadio _textRadio = new() { Text = "文本" };
    private readonly ChipRadio _hexRadio = new() { Text = "HEX", Checked = true };
    private readonly TextBox _payloadBox = new() { Dock = DockStyle.Fill, Multiline = true, Height = 84 };
    private readonly ThemedSelect _checksumCombo = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _delayBox = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 60_000, Value = 0 };
    private readonly Label _errorLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = UiTheme.TextMuted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ThemedButton _okButton = new() { Text = "确定", Kind = ThemedButtonKind.Primary, DialogResult = DialogResult.OK, Width = 88, Height = 36 };
    private readonly Encoding _encoding;

    public DataFrame? Result { get; private set; }

    public FrameEditDialog(DataFrame? existing = null, Encoding? encoding = null)
    {
        _encoding = encoding ?? Encoding.GetEncoding("GBK");

        Text = existing is null ? "新增报文帧" : "编辑报文帧";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 452);
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.UiFont();
        UiTheme.StyleForm(this);

        _checksumCombo.AddRange(ChecksumOptions.Items.Select(i => i.Name).ToArray());
        _checksumCombo.SelectedIndex = existing is null ? 0 : ChecksumOptions.IndexOf(existing.Checksum);
        UiTheme.StyleTextBox(_nameBox);
        UiTheme.StyleTextBox(_payloadBox, mono: true);
        _delayBox.BackColor = UiTheme.SurfaceAlt;
        _delayBox.ForeColor = UiTheme.Text;
        _delayBox.BorderStyle = BorderStyle.FixedSingle;
        _delayBox.Font = UiTheme.UiFont(9f);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = UiTheme.BgElevated,
            Padding = new Padding(24, 16, 24, 10),
        };
        header.Controls.Add(new Label
        {
            Text = existing is null ? "新增报文帧" : "编辑报文帧",
            Dock = DockStyle.Top,
            Height = 32,
            Font = UiTheme.UiFont(15f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        header.Controls.Add(new Label
        {
            Text = "FRAME EDITOR · TEXT / HEX PAYLOAD",
            Dock = DockStyle.Bottom,
            Height = 20,
            Font = UiTheme.UiFont(7.5f, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Padding = new Padding(18, 12, 18, 0) };
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
            RowCount = 6,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
            control.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(control, 1, row);
        }

        AddRow(0, "名称", _nameBox);

        var modeRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        modeRow.Controls.Add(_textRadio);
        modeRow.Controls.Add(new Panel { Width = 6, Height = 1, BackColor = Color.Transparent });
        modeRow.Controls.Add(_hexRadio);
        // 只响应"被选中"的那次:取消选中的 CheckedChanged 也会触发
        _textRadio.CheckedChanged += (_, _) => { if (_textRadio.Checked) OnInputModeChanged(); };
        _hexRadio.CheckedChanged += (_, _) => { if (_hexRadio.Checked) OnInputModeChanged(); };
        AddRow(1, "输入模式", modeRow);
        AddRow(2, "内容", _payloadBox);
        AddRow(3, "校验", _checksumCombo);
        AddRow(4, "帧后延时", _delayBox);
        grid.Controls.Add(_errorLabel, 1, 5);

        card.Controls.Add(grid);
        bodyHost.Controls.Add(card);

        var cancelButton = new ThemedButton
        {
            Text = "取消",
            Kind = ThemedButtonKind.Ghost,
            DialogResult = DialogResult.Cancel,
            Width = 84,
            Height = 36,
            Margin = new Padding(8, 0, 0, 0),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 58,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = UiTheme.Bg,
        };
        buttons.Controls.Add(_okButton);
        buttons.Controls.Add(cancelButton);

        Controls.Add(bodyHost);
        Controls.Add(buttons);
        Controls.Add(header);
        AcceptButton = _okButton;
        CancelButton = cancelButton;

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
            _errorLabel.ForeColor = UiTheme.Success;
            _okButton.Enabled = true;
        }
        else
        {
            _errorLabel.Text = error;
            _errorLabel.ForeColor = UiTheme.Danger;
            _okButton.Enabled = false;
        }
    }

    private void UpdateHint()
    {
        _errorLabel.Text = _hexRadio.Checked
            ? "输入 HEX 字节,可用空格/逗号分隔"
            : "输入文本,支持转义:\\r \\n \\t \\0 \\\\ \\xHH";
        _errorLabel.ForeColor = UiTheme.TextMuted;
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