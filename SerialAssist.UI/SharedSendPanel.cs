using SerialAssist.Core;
using System.Text;

namespace SerialAssist.UI;

/// <summary>共享发送区的目标串口:Send 送字节;Encoding 用委托,每次发送实时取面板当前编码。</summary>
public sealed record SendTarget(string Name, Func<byte[], bool> Send, Func<Encoding> Encoding);

/// <summary>
/// 底部共享发送区(2026-10-14 用户新增):所有串口会话共用一个发送区。
/// 目标串口 = 当前点选的串口窗口(单击选定),发送区顶部显示目标名;文本模式按该口自己的编码转字节。
/// </summary>
internal sealed class SharedSendPanel : UserControl
{
    private SendTarget? _target;
    private readonly Label _targetLabel = null!;
    private readonly TextBox _sendBox = null!;
    private readonly RadioButton _textRadio = null!;
    private readonly RadioButton _hexRadio = null!;
    private readonly ComboBox _lineEndingCombo = null!;
    private readonly Label _previewLabel = null!;
    private readonly Label _statusLabel = null!;
    private bool _suppressRadioConvert;

    /// <summary>用户请求打开数据帧面板(全软件共享一个,MainForm 据此停靠)。</summary>
    public event Action? FramesRequested;

    public SharedSendPanel()
    {
        _statusLabel = new Label { Text = "就绪", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 6, 4, 0) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // ---- 目标行 ----
        var targetRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        _targetLabel = new Label
        {
            Text = "未选择(点一下串口窗口)",
            AutoSize = true,
            ForeColor = Color.Firebrick,
            Margin = new Padding(2, 6, 6, 0),
            Font = new Font(Font, FontStyle.Bold),
        };
        targetRow.Controls.Add(new Label { Text = " 发送到:", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
        targetRow.Controls.Add(_targetLabel);
        targetRow.Controls.Add(_statusLabel);

        // ---- 发送输入区 ----
        _sendBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, Font = new Font("Consolas", 9.5f) };
        // 回车即发送(Shift+Enter 才换行),与原每口发送区习惯一致
        _sendBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                OnSendClicked();
            }
        };
        _textRadio = new RadioButton { Text = "文本", Checked = true, AutoSize = true };
        _hexRadio = new RadioButton { Text = "HEX", AutoSize = true };
        // 切换模式 = 同一份数据换显示形态(文本↔HEX 互转,行尾不掺入,发送时才附加)
        // 只响应"被选中"的那次:取消选中的 CheckedChanged 也会触发,不挡会把刚转的内容转回去
        _textRadio.CheckedChanged += (_, _) => { if (_textRadio.Checked) OnSendModeChanged(toText: true); };
        _hexRadio.CheckedChanged += (_, _) => { if (_hexRadio.Checked) OnSendModeChanged(toText: false); };
        // 行尾默认 CR(多数 CLI 设备只认回车);映射统一走 NewlineOptions,禁止强转索引(顺序错位事故)
        // 仅文本模式生效;HEX 模式下所有口发同一份字节,行尾不参与
        _lineEndingCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _lineEndingCombo.Items.AddRange(NewlineOptions.DisplayNames);
        _lineEndingCombo.SelectedIndex = 0;
        _lineEndingCombo.SelectedIndexChanged += (_, _) => UpdateSendPreview();

        var sendButton = new Button { Text = "发送", Width = 64, Height = 30 };
        sendButton.Click += (_, _) => OnSendClicked();
        var clearSendButton = new Button { Text = "清空", Width = 64, Height = 30 };
        clearSendButton.Click += (_, _) => _sendBox.Clear();
        var fileButton = new Button { Text = "发送文件", Width = 84, Height = 30 };
        fileButton.Click += (_, _) => OnSendFileClicked();
        // 数据帧与发送同属"发什么"一族,入口放发送区;面板全软件一个,发往当前点选的串口
        var frameButton = new Button { Text = "数据帧", Width = 84, Height = 30 };
        frameButton.Click += (_, _) => FramesRequested?.Invoke();

        var modeRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        modeRow.Controls.Add(_textRadio);
        modeRow.Controls.Add(_hexRadio);
        modeRow.Controls.Add(new Label { Text = " 行尾:", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        modeRow.Controls.Add(_lineEndingCombo);

        // 按钮横排放一行:竖排会被面板高度裁掉不可见(踩过两次的坑)
        var buttonRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        buttonRow.Controls.Add(sendButton);
        buttonRow.Controls.Add(clearSendButton);
        buttonRow.Controls.Add(fileButton);
        buttonRow.Controls.Add(frameButton);

        var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        side.Controls.Add(modeRow);
        side.Controls.Add(buttonRow);

        var sendLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        sendLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sendLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sendLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sendLayout.Controls.Add(_sendBox, 0, 0);
        sendLayout.Controls.Add(side, 1, 0);
        var sendGroup = new GroupBox { Text = "发送区(共用,点选目标串口)", Dock = DockStyle.Fill };
        sendGroup.Controls.Add(sendLayout);

        // ---- 预览行 ----
        _previewLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            Height = 20,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Text = "预览:",
            Margin = new Padding(3, 0, 3, 2),
        };
        _sendBox.TextChanged += (_, _) => UpdateSendPreview();

        root.Controls.Add(targetRow, 0, 0);
        root.Controls.Add(sendGroup, 0, 1);
        root.Controls.Add(_previewLabel, 0, 2);
        Controls.Add(root);
        UpdateSendPreview();
    }

    /// <summary>MainForm 在活动窗口变化时调用,更新目标串口(点选哪个串口窗口就是哪个)。</summary>
    public void SetTarget(SendTarget? target)
    {
        _target = target;
        _targetLabel.Text = target?.Name ?? "未选择(点一下串口窗口)";
        _targetLabel.ForeColor = target is null ? Color.Firebrick : Color.FromArgb(0, 70, 130);
        UpdateSendPreview();
    }

    // ---------- 发送 ----------

    private void OnSendClicked()
    {
        if (_target is null)
        {
            SetStatus("先用鼠标点一下要发送的串口窗口", Color.DarkOrange);
            return;
        }
        var newline = NewlineOptions.FromIndex(_lineEndingCombo.SelectedIndex);
        if (_hexRadio.Checked)
        {
            // HEX 模式:解析一次,字节与编码无关
            var payload = SendFormatter.Format(SendMode.Hex, _sendBox.Text, Encoding.ASCII, newline);
            if (!payload.Ok)
            {
                SetStatus($"发送失败:{payload.Error}", Color.Firebrick);
                return;
            }
            var sent = _target.Send(payload.Bytes);
            SetStatus(sent
                ? $"已发送 {payload.Bytes.Length} 字节 → {_target.Name}"
                : $"发送失败:{_target.Name}(串口未打开或被流控阻塞)",
                sent ? Color.DimGray : Color.Firebrick);
            return;
        }

        // 文本模式按目标口自己的编码转字节
        var payloadText = SendFormatter.Format(SendMode.Text, _sendBox.Text, _target.Encoding(), newline);
        if (!payloadText.Ok)
        {
            SetStatus($"发送失败:{payloadText.Error}", Color.Firebrick);
            return;
        }
        var ok = _target.Send(payloadText.Bytes);
        SetStatus(ok
            ? $"已发送 {payloadText.Bytes.Length} 字节 → {_target.Name}"
            : $"发送失败:{_target.Name}(串口未打开或被流控阻塞)",
            ok ? Color.DimGray : Color.Firebrick);
    }

    /// <summary>
    /// 发送文件到当前目标:文本模式 = 文件原始字节 + 行尾下拉所选结尾(选"无"即纯原始字节);
    /// HEX 模式 = 纯原始字节。文件字节不做编码转换。
    /// </summary>
    private void OnSendFileClicked()
    {
        if (_target is null)
        {
            SetStatus("先用鼠标点一下要发送的串口窗口", Color.DarkOrange);
            return;
        }
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
            SetStatus($"读取文件失败:{ex.Message}", Color.Firebrick);
            return;
        }
        if (bytes.Length == 0)
        {
            SetStatus("文件为空,未发送", Color.DarkOrange);
            return;
        }

        var fileBytes = bytes;
        var endingNote = string.Empty;
        if (_textRadio.Checked)
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

        var head = HexCodec.ToHexString(fileBytes[..Math.Min(16, fileBytes.Length)]);
        var ellipsis = fileBytes.Length > 16 ? " …" : string.Empty;
        if (_target.Send(fileBytes))
        {
            SetStatus($"已发送 {Path.GetFileName(dialog.FileName)}({fileBytes.Length} 字节{endingNote}) → {_target.Name}:{head}{ellipsis}", Color.DimGray);
        }
        else
        {
            SetStatus($"文件发送失败:{_target.Name}(串口未打开或被流控阻塞)", Color.Firebrick);
        }
    }

    /// <summary>
    /// 切换发送模式:同一份数据换显示形态(文本↔HEX 互转,行尾不掺入)。
    /// 转换编码取当前目标口的编码(没有目标退回 GBK);HEX 转不动时弹回 HEX 模式,内容不丢。
    /// </summary>
    private void OnSendModeChanged(bool toText)
    {
        UpdateSendPreview();
        if (_suppressRadioConvert || string.IsNullOrWhiteSpace(_sendBox.Text))
        {
            return;
        }
        var enc = PreviewSource().Enc;
        if (toText)
        {
            if (!HexCodec.TryParse(_sendBox.Text, out var bytes, out var error))
            {
                SetStatus($"转换失败:{error}(内容未变)", Color.Firebrick);
                _suppressRadioConvert = true;
                _hexRadio.Checked = true; // 弹回 HEX 模式
                _suppressRadioConvert = false;
                return;
            }
            _sendBox.Text = enc.GetString(bytes);
        }
        else
        {
            _sendBox.Text = HexCodec.ToHexString(enc.GetBytes(_sendBox.Text));
        }
    }

    /// <summary>预览基准:当前目标口的编码;没有目标退回 GBK。</summary>
    private (SendTarget? Target, Encoding Enc) PreviewSource()
    {
        if (_target is null)
        {
            Encoding gbk;
            try
            {
                gbk = Encoding.GetEncoding("GBK");
            }
            catch (ArgumentException)
            {
                gbk = Encoding.ASCII;
            }
            return (null, gbk);
        }
        return (_target, _target.Encoding());
    }

    /// <summary>发送框实时预览:文本模式显示将发送字节的 HEX(按预览目标编码),HEX 模式显示解码文本。</summary>
    private void UpdateSendPreview()
    {
        if (_previewLabel is null)
        {
            return; // 构建顺序防 null
        }
        const int MaxPreviewChars = 512;
        var mode = _hexRadio.Checked ? SendMode.Hex : SendMode.Text;
        var (target, enc) = PreviewSource();
        var preview = SendPreview.Render(mode, _sendBox.Text, enc, NewlineOptions.FromIndex(_lineEndingCombo.SelectedIndex));
        if (preview.Length > MaxPreviewChars)
        {
            preview = preview[..MaxPreviewChars] + " …";
        }
        var head = target is null ? string.Empty : $"(以 {target.Name} 的编码)";
        _previewLabel.Text = preview.Length == 0 ? "预览:" : $"预览{head}: {preview}";
    }

    private void SetStatus(string text, Color color)
    {
        if (!_statusLabel.IsDisposed)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }
    }
}
