using System.Text;
using SerialAssist.Core;

namespace SerialAssist.UI;

static class Program
{
    private static void ReportCrash(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppPaths.DataDir, "crash.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
            MessageBox.Show($"{ex.GetType().Name}: {ex.Message}\n\n详细信息已记录到:\n{logPath}",
                "程序异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // 日志失败也不能吞掉异常本身
            MessageBox.Show($"{ex.GetType().Name}: {ex.Message}", "程序异常",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // GBK 等代码页在 .NET Core 需显式注册(工位设备中文回显依赖 GBK)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // 启动即确定数据目录(便携 data / LocalAppData),配置落盘统一走 AppPaths
        AppPaths.EnsureCreated();
        ApplicationConfiguration.Initialize();
        // UI 线程未处理异常:落日志 + 弹窗,不静默丢失现场
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.Run(new MainForm());
    }
}