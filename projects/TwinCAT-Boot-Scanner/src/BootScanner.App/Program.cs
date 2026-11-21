using System.Runtime.InteropServices;

namespace BootScanner.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowCrash(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ShowCrash(ex);
        }
    }

    private static void ShowCrash(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? Path.GetTempPath();
            var log = Path.Combine(dir, "BootScanner-error.txt");
            File.WriteAllText(log, DateTime.Now + Environment.NewLine + ex);
            MessageBox.Show(
                ex.Message + Environment.NewLine + Environment.NewLine + "详细已写入:" + Environment.NewLine + log,
                "Boot 查看器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            try
            {
                MessageBox.Show(ex.ToString(), "Boot 查看器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // last resort: keep process from silently disappearing
            }
        }
    }

    /// <summary>
    /// Ask Windows 10/11 for a dark title bar. Failures are ignored — a normal caption is fine.
    /// </summary>
    internal static void TryEnableDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            var useDark = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1+); 19 is the older value.
            if (DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
        }
        catch
        {
            // DWM not available — keep the default title bar.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
