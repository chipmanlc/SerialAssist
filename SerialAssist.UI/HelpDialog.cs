namespace SerialAssist.UI;

/// <summary>
/// 操作说明(模态框):只读富文本,分节列出已实现功能的用法。
/// 标题行加粗深蓝,正文黑,随窗体缩放可滚动。
/// </summary>
internal sealed class HelpDialog : Form
{
    public HelpDialog()
    {
        Text = "操作说明";
        Font = new Font("Microsoft YaHei UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(680, 680);

        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.None,
        };
        var closeButton = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Width = 88, Height = 28 };
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        bottom.Controls.Add(closeButton);
        Controls.Add(box);
        Controls.Add(bottom);
        CancelButton = closeButton;

        AppendManual(box);
    }

    private static void AppendManual(RichTextBox box)
    {
        Heading("CHEN串口助手 操作说明");
        Blank();

        Heading("一、新建串口会话");
        Body("· 工具栏「+」新建串口面板:端口/波特率/校验位/数据位/停止位/流控/日志在对话框里一次配齐,创建即自动打开串口。");
        Body("· 可同时开多个串口,面板自动并排停靠,可拖动分屏/浮动,页签显示端口名与会话状态。");
        Body("· 接收区工具栏左侧的小按钮用于手动关闭/重开当前串口。");
        Body("· 流控在新建对话框里选(默认无),对端要求 DTR/RTS 时保持默认使能即可。");
        Blank();

        Heading("二、接收区");
        Body("· 编码下拉:GBK(默认)/UTF-8/ASCII,切换后从原始字节重新投影,不丢数据。");
        Body("· 文本/HEX 显示一键切换;时间戳开关(毫秒精度)。");
        Body("· 自动跟随:正常状态自动滚到最新;鼠标悬停显示「悬停预览中」;拖滚动条上翻历史显示「回滚预览中」;拖回底部自动恢复跟随刷新。回滚态下悬停不会顶掉回滚提示。");
        Body("· Ctrl+滚轮缩放字体;「清除屏幕&回滚」清屏但保留缩放;「跳到最新」一键回底部。");
        Body("· 数据量过大时视图自动跳尾防卡顿(状态栏有提示),完整数据仍在接收日志里。");
        Blank();

        Heading("三、发送区");
        Body("· 文本/HEX 即点即转:内容自动互转,HEX 转不动时弹错且原内容不丢。");
        Body("· 回车即发送,Shift+Enter 换行;行尾下拉 CR(默认)/LF/CRLF/无,仅文本模式附加,HEX 模式字节精确不附加。");
        Body("· 发送框下方实时预览将发出的字节(含行尾),所见即所发。");
        Body("· 「发送文件」按原始字节发送文件内容;文本模式可选附加行尾;状态栏显示字节数与 HEX 预览。");
        Body("· 串口被流控阻塞时发送超时 2 秒即报错,界面不会卡死,状态栏给出失败原因。");
        Blank();

        Heading("四、数据帧");
        Body("· 接收区工具栏「数据帧」打开帧面板,停靠在会话右侧。");
        Body("· 新增帧:文本(支持转义 \\r \\n \\t \\0 \\a \\b \\f \\v \\\\ \\xHH)或 HEX 两种输入,实时显示字节数与预览。");
        Body("· 校验可附:Sum8 / CRC8(多项式0x07) / CRC16-Modbus(低字节在前),校验字节自动追加在 payload 后。");
        Body("· 帧后延时:连发/循环时该帧发完等待的毫秒数。");
        Body("· 勾选=启用:单发优先发勾选帧(无勾选则发蓝色选中帧);连发=所有启用帧按顺序一遍;循环=可启停的循环发送(以启动时列表为准)。");
        Body("· 上移/下移调整帧顺序,双击帧可编辑。");
        Blank();

        Heading("五、日志与数据目录");
        Body("· 会话日志:新建面板时在对话框里勾选并选路径,日志与视图独立推进,翻历史/悬停期间照记不漏。");
        Body("· 便携模式:exe 所在目录可写时,数据存放在 exe 旁的 data\\ 目录(随 U 盘走);不可写时自动改用 %LOCALAPPDATA%\\SerialAssistPro\\data。");
        Body("· 程序异常时的 crash.log 也写在数据目录,报问题时请带上。");
        Blank();

        Heading("六、提示汇总");
        Body("· 悬停预览中 = 鼠标停在接收区上,暂停跟随方便查看。");
        Body("· 回滚预览中 = 你正在上翻历史,新数据在后台照常接收,只是不自动滚到底。");
        Body("· 状态栏还会显示发送结果、编码、跳尾等提示,出问题时先看状态栏。");

        void Heading(string text)
        {
            AppendLine(box, text, FontStyle.Bold, Color.FromArgb(0, 70, 130));
        }

        void Body(string text)
        {
            AppendLine(box, text, FontStyle.Regular, Color.Black);
        }

        void Blank()
        {
            AppendLine(box, string.Empty, FontStyle.Regular, Color.Black);
        }

        void AppendLine(RichTextBox box, string text, FontStyle style, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionFont = new Font(box.Font, style);
            box.SelectionColor = color;
            box.AppendText(text + Environment.NewLine);
        }
    }
}
