using System;
using System.Diagnostics;

namespace FpsSocdTool
{
    /// <summary>
    /// Optional foreground-process allow list. An empty list means "all apps".
    /// The tiny cache keeps process lookups out of the keyboard hook hot path.
    /// </summary>
    internal sealed class ProcessFilter
    {
        private volatile string[] _names = new string[0];
        private IntPtr _lastWindow;
        private int _lastCheckTick;
        private bool _lastResult;
        private bool _loggedUnreadable;

        internal void SetList(string value)
        {
            _lastWindow = IntPtr.Zero;
            _lastCheckTick = 0;
            _lastResult = false;
            _loggedUnreadable = false;

            if (string.IsNullOrWhiteSpace(value))
            {
                _names = new string[0];
                return;
            }

            string[] parts = value.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            string[] names = new string[parts.Length];
            int count = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - 4);
                }

                if (name.Length > 0)
                {
                    names[count] = name.ToLowerInvariant();
                    count++;
                }
            }

            if (count == 0)
            {
                _names = new string[0];
                return;
            }

            string[] trimmed = new string[count];
            Array.Copy(names, trimmed, count);
            _names = trimmed;
        }

        internal bool IsAllowed()
        {
            string[] names = _names;
            if (names.Length == 0)
            {
                return true;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            int now = Environment.TickCount;
            if (foreground == _lastWindow && unchecked(now - _lastCheckTick) < 500)
            {
                return _lastResult;
            }

            bool allowed = false;
            try
            {
                uint processId;
                NativeMethods.GetWindowThreadProcessId(foreground, out processId);
                if (processId != 0)
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        string processName = (process.ProcessName ?? string.Empty).ToLowerInvariant();
                        for (int i = 0; i < names.Length; i++)
                        {
                            if (string.Equals(processName, names[i], StringComparison.OrdinalIgnoreCase))
                            {
                                allowed = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Some protected game processes cannot be inspected. Failing
                // open keeps the tool usable when the user configured a process
                // filter for such a game; the filter is best-effort anyway.
                allowed = true;
                if (!_loggedUnreadable)
                {
                    _loggedUnreadable = true;
                    Log.Warn("OnlyInProcess 已配置，但无法读取前台进程名（可能被反作弊保护）；本次放行。");
                }
            }

            _lastWindow = foreground;
            _lastCheckTick = now;
            _lastResult = allowed;
            return allowed;
        }
    }
}
