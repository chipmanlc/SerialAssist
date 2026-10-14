using SerialAssist.Core;
using System.Text;

namespace SerialAssist.UI;

/// <summary>共享发送区的目标串口:Send 送字节;Encoding 用委托,每次发送实时取面板当前编码。</summary>
public sealed record SendTarget(string Name, Func<byte[], bool> Send, Func<Encoding> Encoding);

/// <summary>
/// 底部共享发送区(2026-10-14 用户新增):所有串口会话共用一个发送区。
/// 目标串口用勾选框多选(全选/全不选),文本模式按每个目标自己的编码转字节,HEX 模式同一份字节广播。
/// </summary>
internal sealed class SharedSendPanel : UserControl
{
    private List<SendTarget> _targets = new();
    private readonly List<CheckBox> _targetChecks = new();
    private readonly FlowLayoutPanel _checkHost = null!;
    private readonly TextBox _sendBox = null!;
    private readonly RadioButton _textRadio = null!;
    private readonly RadioButton _hexRadio = null!;
    private readonly ComboBox _lineEndingCombo = null!;
    private readonly Label _previewLabel = null!;
    private readonly Label _statusLabel = null!;
    private bool _suppressRadioConvert;

    public SharedSendPanel()
    {
        _statusLabel = new Label { Text = "就绪", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 6, 4, 0) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // ---- 目标勾选行 ----
        var targetRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        _checkHost = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        var selectAll = new Button { Text = "全选", Width = 52, Height = 24 };
        var selectNone = new Button { Text = "全不选", Width = 64, Height = 24 };
        selectAll.Click += (_, _) => SetAllTargets(true);
        selectNone.Click += (_, _) => SetAllTargets(false);
        targetRow.Controls.Add(new Label { Text = " 发送到:", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
        targetRow.Controls.Add(_checkHost);
        targetRow.Controls.Add(selectAll);
        targetRow.Controls.Add(selectNone);
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

        var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        side.Controls.Add(modeRow);
        side.Controls.Add(buttonRow);

        var sendLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        sendLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sendLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sendLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sendLayout.Controls.Add(_sendBox, 0, 0);
        sendLayout.Controls.Add(side, 1, 0);
        var sendGroup = new GroupBox { Text = "发送区(共用,先勾选目标串口)", Dock = DockStyle.Fill };
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

    /// <summary>MainForm 在会话开/关时调用,重建勾选框;按名字保留原勾选状态。</summary>
    public void SetTargets(IReadOnlyList<SendTarget> targets)
    {
        _targets = targets.ToList();
        var hadChecks = _targetChecks.Count > 0; // 之前有没有勾选框:区分"首次默认勾"与"用户主动全不选"
        var checkedNames = _targetChecks.Where(c => c.Checked && c.Tag is string).Select(c => (string)c.Tag!).ToHashSet();
        _checkHost.Controls.Clear();
        _targetChecks.Clear();
        foreach (var target in _targets)
        {
            var check = new CheckBox { Text = target.Name, AutoSize = true, Tag = target.Name, Margin = new Padding(2, 4, 6, 0) };
            check.CheckedChanged += (_, _) => UpdateSendPreview();
            _checkHost.Controls.Add(check);
            _targetChecks.Add(check);
        }
        if (!hadChecks && _targetChecks.Count > 0)
        {
            // 首个串口建立时默认勾上,降低"没勾目标就点发送"的空手率
            _targetChecks[0].Checked = true;
        }
        else
        {
            foreach (var check in _targetChecks)
            {
                check.Checked = checkedNames.Contains((string)check.Tag!);
            }
        }
        UpdateSendPreview();
    }

    private List<SendTarget> CheckedTargets =>
        _targets.Where((_, i) => i < _targetChecks.Count && _targetChecks[i].Checked).ToList();

    private void SetAllTargets(bool checkd)
    {
        foreach (var check in _targetChecks)
        {
            check.Checked = checkd;
        }
    }

    // ---------- 发送 ----------

    private void OnSendClicked()
    {
        var targets = CheckedTargets;
        if (targets.Count == 0)
        {
            SetStatus("先勾选要发送的串口", Color.DarkOrange);
            return;
        }
        var newline = NewlineOptions.FromIndex(_lineEndingCombo.SelectedIndex);
        var okNames = new List<string>();
        var failNames = new List<string>();
        var byteLen = 0;
        if (_hexRadio.Checked)
        {
            // HEX 模式只解析一次,同一份字节广播到所有勾选目标(与编码无关)
            var payload = SendFormatter.Format(SendMode.Hex, _sendBox.Text, Encoding.ASCII, newline);
            if (!payload.Ok)
            {
                SetStatus($"发送失败:{payload.Error}", Color.Firebrick);
                return;
            }
            byteLen = payload.Bytes.Length;
            foreach (var target in targets)
            {
                (target.Send(payload.Bytes) ? okNames : failNames).Add(target.Name);
            }
        }
        else
        {
            // 文本模式按每个目标自己的编码转字节(各口设备编码可能不同)
            foreach (var target in targets)
            {
                var payload = SendFormatter.Format(SendMode.Text, _sendBox.Text, target.Encoding(), newline);
                if (!payload.Ok)
                {
                    failNames.Add($"{target.Name}:{payload.Error}");
                    continue;
                }
                byteLen = payload.Bytes.Length;
                (target.Send(payload.Bytes) ? okNames : failNames).Add(target.Name);
            }
        }

        if (failNames.Count == 0)
        {
            SetStatus($"已发送 {byteLen} 字节 → {string.Join("、", okNames)}", Color.DimGray);
        }
        else
        {
            SetStatus($"成功 {okNames.Count}/{targets.Count};失败:{string.Join("; ", failNames)}(串口未打开或被流控阻塞)",
                okNames.Count > 0 ? Color.DarkOrange : Color.Firebrick);
        }
    }

    /// <summary>
    /// 发送文件到所有勾选目标:文本模式 = 文件原始字节 + 行尾下拉所选结尾(选"无"即纯原始字节);
    /// HEX 模式 = 纯原始字节。文件字节不做编码转换,与原每口发送文件规则一致。
    /// </summary>
    private void OnSendFileClicked()
    {
        var targets = CheckedTargets;
        if (targets.Count == 0)
        {
            SetStatus("先勾选要发送的串口", Color.DarkOrange);
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

        var okNames = new List<string>();
        var failNames = new List<string>();
        foreach (var target in targets)
        {
            (target.Send(fileBytes) ? okNames : failNames).Add(target.Name);
        }
        var head = HexCodec.ToHexString(fileBytes[..Math.Min(16, fileBytes.Length)]);
        var ellipsis = fileBytes.Length > 16 ? " …" : string.Empty;
        if (failNames.Count == 0)
        {
            SetStatus($"已发送 {Path.GetFileName(dialog.FileName)}({fileBytes.Length} 字节{endingNote}) → {string.Join("、", okNames)}:{head}{ellipsis}", Color.DimGray);
        }
        else
        {
            SetStatus($"文件发送成功 {okNames.Count}/{targets.Count};失败:{string.Join("、", failNames)}", okNames.Count > 0 ? Color.DarkOrange : Color.Firebrick);
        }
    }

    /// <summary>
    /// 切换发送模式:同一份数据换显示形态(文本↔HEX 互转,行尾不掺入)。
    /// 转换编码取"预览目标"(第一个勾选口,没有则第一个目标);HEX 转不动时弹回 HEX 模式,内容不丢。
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

    /// <summary>预览基准:第一个勾选目标的编码;没有勾选则用第一个目标;连目标都没有退回 GBK。</summary>
    private (SendTarget? Target, Encoding Enc) PreviewSource()
    {
        var target = CheckedTargets.FirstOrDefault() ?? _targets.FirstOrDefault();
        if (target is null)
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
        return (target, target.Encoding());
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
