using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FpsSocdTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool dryRun = false;
            bool selfTest = false;
            bool showMenuOnStart = false;
            bool debugInput = false;
            string configPath = null;
            string selfTestReport = null;

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                if (string.Equals(argument, "--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    selfTest = true;
                }
                else if (string.Equals(argument, "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                }
                else if (string.Equals(argument, "--show-menu", StringComparison.OrdinalIgnoreCase))
                {
                    showMenuOnStart = true;
                }
                else if (string.Equals(argument, "--debug-input", StringComparison.OrdinalIgnoreCase))
                {
                    debugInput = true;
                }
                else if (string.Equals(argument, "--start-enabled", StringComparison.OrdinalIgnoreCase))
                {
                    StartupOptions.ForceStartEnabled = true;
                }
                else if (argument.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                {
                    configPath = argument.Substring("--config=".Length);
                }
                else if (argument.StartsWith("--report=", StringComparison.OrdinalIgnoreCase))
                {
                    selfTestReport = argument.Substring("--report=".Length);
                }
            }

            if (selfTest)
            {
                Environment.Exit(SelfTest.Run(selfTestReport));
                return;
            }

            DebugTrace.Enabled = debugInput;
            DebugTrace.Reset();
            DebugTrace.Write("启动 PID=" + System.Diagnostics.Process.GetCurrentProcess().Id +
                " 版本=" + typeof(Program).Assembly.GetName().Version);

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            // Keep user-writable data out of the install directory. This avoids
            // write restrictions on C:\ root and follows Windows conventions.
            string dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FpsSocdTool");
            try
            {
                Directory.CreateDirectory(dataDirectory);
                string writeTest = Path.Combine(dataDirectory, ".write-test");
                File.WriteAllText(writeTest, "ok");
                File.Delete(writeTest);
            }
            catch
            {
                // If the profile folder is not writable, keep the portable
                // config that sits next to the executable.
                dataDirectory = baseDirectory;
            }

            string logPath = Path.Combine(dataDirectory, "socd.log");
            Log.Init(logPath);

            if (configPath == null || configPath.Trim().Length == 0)
            {
                string legacyConfig = Path.Combine(baseDirectory, "config.ini");
                configPath = Path.Combine(dataDirectory, "config.ini");
                try
                {
                    if (!File.Exists(configPath) && File.Exists(legacyConfig))
                    {
                        // Preserve settings from the portable/old layout.
                        File.Copy(legacyConfig, configPath, false);
                        Log.Info("已将旧配置迁移到：" + configPath);
                    }
                }
                catch (Exception exception)
                {
                    Log.Warn("迁移旧配置失败：" + exception.Message);
                }
            }

            try
            {
                configPath = Path.GetFullPath(configPath);
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            string mutexName = "Local\\FpsSocdTool.SingleInstance" + (dryRun ? ".dryrun" : string.Empty);
            Mutex singleInstance = null;
            try
            {
                bool createdNew;
                try
                {
                    singleInstance = new Mutex(true, mutexName, out createdNew);
                }
                catch (UnauthorizedAccessException exception)
                {
                    Log.Error("无法访问单实例锁（可能已有一个管理员权限实例在运行）", exception);
                    MessageBox.Show("无法访问单实例锁。\n\n可能已经有一个以管理员权限运行的工具实例；" +
                        "请检查系统托盘，或以相同的管理员权限运行本程序。",
                        "FPS SOCD 急停助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                catch (Exception exception)
                {
                    Log.Error("创建单实例锁失败", exception);
                    MessageBox.Show("创建单实例锁失败：\n" + exception.Message,
                        "FPS SOCD 急停助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!createdNew)
                {
                    // A second launch is the fallback way to reach the menu when
                    // Windows has hidden the tray icon in the overflow area.
                    IntPtr controlWindow = NativeMethods.FindWindow(null, NativeMethods.ControlWindowTitle);
                    bool menuRequestPosted = false;
                    if (controlWindow != IntPtr.Zero)
                    {
                        menuRequestPosted = NativeMethods.PostMessage(controlWindow,
                            NativeMethods.ShowMenuMessage, IntPtr.Zero, IntPtr.Zero);
                    }

                    if (!menuRequestPosted && NativeMethods.ShowMenuMessage != 0)
                    {
                        menuRequestPosted = NativeMethods.PostMessage(NativeMethods.HwndBroadcast,
                            NativeMethods.ShowMenuMessage, IntPtr.Zero, IntPtr.Zero);
                    }

                    if (menuRequestPosted)
                    {
                        Log.Info("已通知正在运行的实例显示控制菜单");
                        return;
                    }

                    MessageBox.Show("FPS SOCD 急停助手已经在运行。\n请查看系统托盘图标。",
                        "FPS SOCD 急停助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                TrayAppContext context = null;
                try
                {
                    context = new TrayAppContext(dryRun, configPath, showMenuOnStart);
                    Application.Run(context);
                }
                catch (Exception exception)
                {
                    Log.Error("程序运行失败", exception);
                    try
                    {
                        MessageBox.Show("FPS SOCD 急停助手启动失败：\n\n" + exception.Message +
                            "\n\n日志文件：\n" + Log.FilePath, "FPS SOCD 急停助手",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    if (context != null)
                    {
                        context.Cleanup();
                    }
                }
            }
            finally
            {
                if (singleInstance != null)
                {
                    singleInstance.Dispose();
                }
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Error("界面线程未处理异常", e.Exception);
            try
            {
                MessageBox.Show("工具遇到一个错误：\n\n" + e.Exception.Message + "\n\n详情见日志：\n" + Log.FilePath,
                    "FPS SOCD 急停助手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch
            {
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;
            if (exception != null)
            {
                Log.Error("未处理异常", exception);
            }
            else
            {
                Log.Error("未处理异常：" + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "未知"));
            }
        }
    }
}
