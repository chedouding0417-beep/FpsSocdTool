using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Diagnostics;

internal sealed class LauncherService : ServiceBase
{
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const int TokenLinkedToken = 19;
    private const int TokenSessionId = 12;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int WTSActive = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_LINKED_TOKEN
    {
        public IntPtr LinkedToken;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, out TOKEN_LINKED_TOKEN info, int length, out int returned);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, ref int info, int length, out int returned);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr token, int informationClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess,
        IntPtr tokenAttributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateProcessAsUser(IntPtr token, string applicationName, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll")]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out int exitCode);

    private static readonly string LogPath = @"C:\FpsSocdTool\service.log";
    private readonly object _gate = new object();
    private readonly HashSet<int> _launched = new HashSet<int>();
    private Thread _worker;
    private volatile bool _stopping;

    public LauncherService()
    {
        ServiceName = "FpsSocdLauncherService";
        CanStop = true;
        CanShutdown = true;
    }

    private static void Write(string message)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("o") + " " + message + Environment.NewLine, new UTF8Encoding(false)); } catch { }
    }

    protected override void OnStart(string[] args)
    {
        Write("service start");
        _stopping = false;
        _worker = new Thread(Worker);
        _worker.IsBackground = true;
        _worker.Start();
    }

    protected override void OnStop()
    {
        Write("service stop");
        _stopping = true;
        if (_worker != null) { _worker.Join(2000); }
    }

    private void Worker()
    {
        while (!_stopping)
        {
            try { CheckSessions(); } catch (Exception e) { Write("check error: " + e); }
            Thread.Sleep(2000);
        }
    }

    private void CheckSessions()
    {
        IntPtr buffer;
        int count;
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out buffer, out count)) return;
        try
        {
            int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
            for (int i = 0; i < count; i++)
            {
                WTS_SESSION_INFO info = (WTS_SESSION_INFO)Marshal.PtrToStructure(
                    new IntPtr(buffer.ToInt64() + i * size), typeof(WTS_SESSION_INFO));
                if (info.State != WTSActive) continue;
                lock (_gate)
                {
                    if (_launched.Contains(info.SessionId)) continue;
                }
                if (Launch(info.SessionId))
                {
                    lock (_gate) { _launched.Add(info.SessionId); }
                }
            }
        }
        finally { WTSFreeMemory(buffer); }
    }

    private bool Launch(int sessionId)
    {
        IntPtr userToken;
        if (!WTSQueryUserToken((uint)sessionId, out userToken))
        {
            Write("WTSQueryUserToken failed session=" + sessionId + " err=" + Marshal.GetLastWin32Error());
            return false;
        }

        IntPtr linkedToken = IntPtr.Zero;
        TOKEN_LINKED_TOKEN linked;
        int returned;
        if (GetTokenInformation(userToken, TokenLinkedToken, out linked, Marshal.SizeOf(typeof(TOKEN_LINKED_TOKEN)), out returned) && linked.LinkedToken != IntPtr.Zero)
        {
            linkedToken = linked.LinkedToken;
        }

        IntPtr useToken = linkedToken != IntPtr.Zero ? linkedToken : userToken;
        IntPtr linkedPrimary = IntPtr.Zero;
        if (DuplicateTokenEx(useToken, 0, IntPtr.Zero, 2, 1, out linkedPrimary))
        {
            useToken = linkedPrimary;
            int sidForToken = sessionId;
            SetTokenInformation(useToken, TokenSessionId, ref sidForToken, 4);
        }
        IntPtr environment = IntPtr.Zero;
        CreateEnvironmentBlock(out environment, useToken, false);

        STARTUPINFO si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        si.lpDesktop = "winsta0\\default";
        PROCESS_INFORMATION pi;
        string exe = @"C:\FpsSocdTool\FpsSocdTool.exe";
        string cmd = "\"" + exe + "\"";
        bool ok = CreateProcessAsUser(useToken, exe, cmd, IntPtr.Zero, IntPtr.Zero, false,
            CREATE_UNICODE_ENVIRONMENT, environment, @"C:\FpsSocdTool", ref si, out pi);
        int err = Marshal.GetLastWin32Error();
        Write("CreateProcessAsUser session=" + sessionId + " linked=" + (linkedToken != IntPtr.Zero) +
            " ok=" + ok + " err=" + err + (ok ? (" pid=" + pi.dwProcessId) : ""));

        if (ok) { WaitForSingleObject(pi.hProcess, 5000); int exitCode; GetExitCodeProcess(pi.hProcess, out exitCode); Write("app exit code=" + exitCode + " alive=" + (exitCode == 259)); CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
        if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
        if (linkedPrimary != IntPtr.Zero) CloseHandle(linkedPrimary);
        if (linkedToken != IntPtr.Zero) CloseHandle(linkedToken);
        CloseHandle(userToken);
        if (ok) return true;

        // Last-resort: system token in the target session.
        IntPtr processToken;
        if (OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY, out processToken))
        {
            IntPtr primary;
            if (DuplicateTokenEx(processToken, 0, IntPtr.Zero, 2, 1, out primary))
            {
                int sid = sessionId;
                SetTokenInformation(primary, TokenSessionId, ref sid, 4);
                si.lpDesktop = "winsta0\\default";
                bool sysOk = CreateProcessAsUser(primary, exe, cmd, IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, @"C:\FpsSocdTool", ref si, out pi);
                Write("system-token CreateProcessAsUser ok=" + sysOk + " err=" + Marshal.GetLastWin32Error() +
                    (sysOk ? (" pid=" + pi.dwProcessId) : ""));
                if (sysOk) { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
                CloseHandle(primary);
            }
            CloseHandle(processToken);
        }
        return false;
    }

    public static void Main()
    {
        ServiceBase.Run(new LauncherService());
    }
}
