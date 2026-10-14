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
        Heading("CHEN串口助手 使用说明");
        Blank();

        Heading("一、开始使用");
        Body("· 点左上角「+」,选好串口号和波特率等参数,确定后会自动连上串口开始收发。");
        Body("· 可以同时开多个串口,每个串口一个页面。点工具栏「垂直平铺」,所有窗口排成两列网格(3 个=上二下一,4 个=上二下二……),一屏全见;窗口变窄时接收区的按钮会自动折行,功能不缺。");
        Body("· 一般参数保持默认即可;连不上或收不到数据时,先检查串口号和波特率是否与设备一致。");
        Blank();

        Heading("二、看数据(接收区)");
        Body("· 收到的内容会自动滚动显示,始终停在最新一行。");
        Body("· 想仔细看某段内容:鼠标停在上面,或用滚动条往上翻,新数据不会把画面拽走;状态栏会显示「悬停预览中」或「回滚预览中」。");
        Body("· 看完了:把滚动条拉回最底下,或点「跳到最新」,就恢复自动滚动。");
        Body("· 字太小:按住 Ctrl 再滚滚轮,可以放大缩小。");
        Body("· 「清除屏幕&回滚」:把画面清空重来,你调好的字体大小会保留。");
        Body("· 中文乱码:换一下编码(GBK / UTF-8 / ASCII)再试。");
        Body("· 可以切换 文本 / HEX 两种显示,也可以开关时间戳。");
        Body("· 数据特别快时画面会自动跳到最新,中间部分可能略过;需要完整记录请开日志(见第六节)。");
        Blank();

        Heading("三、发数据(底部共享发送区)");
        Body("· 发送框在窗口最底部,所有串口共用一个。用鼠标点一下某个串口窗口(点它的标题或页面任意位置),就选定它为发送目标,发送区顶部会显示当前目标串口名。");
        Body("· 在发送框输入内容,按回车直接发送;按 Shift+回车 是换一行,不发送。");
        Body("· 发十六进制就点 HEX,发文字就点文本,两种格式可以一键互转。");
        Body("· 文字会按每个串口自己的编码设置转成字节;HEX 模式发的是同一份字节,和编码无关。");
        Body("· 想让结尾自动带回车:行尾选 CR / LF / CRLF;选「无」就不加任何东西。发 HEX 时不会自动加结尾。");
        Body("· 发送框下方会显示「这次实际发出的字节」,发之前可以核对一遍。");
        Body("· 发文件:点「发送文件」选文件,文件内容原样发给当前点选的串口。");
        Body("· 发送失败时状态栏会写明哪个串口失败、什么原因(比如串口没打开)。");
        Blank();

        Heading("四、数据帧(常用命令收藏)");
        Body("· 点发送区里的「数据帧」按钮,弹出一个独立的「数据帧」小窗口(全软件共用一个,不占串口窗口的地方),把常用的命令存起来,以后一点就发,不用重复输入。");
        Body("· 帧发往「当前点选的串口」,跟发送区是同一个目标:点哪个串口窗口,帧就发到哪个串口。");
        Body("· 新增帧时可以填文字(支持 \\r \\n 等转义符,写法见编辑框里的说明)或 HEX。");
        Body("· 需要校验的设备,选好校验方式(Sum8 / CRC8 / CRC16-Modbus),程序会自动算好拼在命令后面,不用自己算。");
        Body("· 「单发」= 发勾选的帧;「连发」= 把勾选的帧按顺序各发一遍;「循环」= 反复发送,再点一次就停。");
        Body("· 每条帧可以设「发完后等多久再发下一条」,单位毫秒。");
        Body("· 双击一条帧可以修改,上下移按钮调整顺序。");
        Blank();

        Heading("五、状态栏提示是什么意思");
        Body("· 悬停预览中:鼠标停在接收区上,方便你查看,新数据暂时不上滚。");
        Body("· 回滚预览中:你正在翻历史记录,新数据照常在后台接收,只是不打扰你看。");
        Body("· 其他橙色/红色提示:发送结果、失败原因等都会写在这里,出问题先看它。");
        Blank();

        Heading("六、日志");
        Body("· 新建串口时勾选「记录日志」并选好保存位置,收到的所有数据都会存成文件,随时可以翻查。");
        Body("· 日志和画面是分开的:你在翻历史、看预览时,日志照样完整记录,一条不漏。");
        Blank();

        Heading("七、其他");
        Body("· 绿色软件:整个程序就一个 exe,拷到 U 盘也能用;设置和数据存在 exe 旁边的 data 文件夹里。");
        Body("· 程序意外退出时会在 data 文件夹里生成 crash.log,反馈问题时请带上这个文件。");

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
