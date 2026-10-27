using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class UacLauncher
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static string LogPath = @"C:\FpsSocdTool\uac-launcher.log";

    private static void Write(string message)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("o") + " " + message + Environment.NewLine, new UTF8Encoding(false)); } catch { }
    }

    [STAThread]
    private static void Main()
    {
        Write("launcher start");
        Thread.Sleep(5000);
        SHELLEXECUTEINFO info = new SHELLEXECUTEINFO();
        info.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
        info.fMask = 0x00000040; // SEE_MASK_NOCLOSEPROCESS
        info.lpVerb = "runas";
        info.lpFile = @"C:\FpsSocdTool\FpsSocdTool.exe";
        info.lpDirectory = @"C:\FpsSocdTool";
        info.nShow = 1;
        bool ok = ShellExecuteEx(ref info);
        int err = Marshal.GetLastWin32Error();
        Write("ShellExecuteEx ok=" + ok + " err=" + err);
        if (info.hProcess != IntPtr.Zero) CloseHandle(info.hProcess);
    }
}