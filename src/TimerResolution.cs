using System;
using System.Runtime.InteropServices;

namespace FpsSocdTool
{
    /// <summary>
    /// Raises the Windows timer resolution to 1 ms while the tool is running so
    /// very short TapMs values (1-5 ms) are released close to their target time.
    /// </summary>
    internal static class TimerResolution
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint milliseconds);

        private static readonly object Gate = new object();
        private static bool _active;

        internal static void Begin()
        {
            lock (Gate)
            {
                if (_active)
                {
                    return;
                }

                try
                {
                    TimeBeginPeriod(1);
                    _active = true;
                }
                catch
                {
                }
            }
        }

        internal static void End()
        {
            lock (Gate)
            {
                if (!_active)
                {
                    return;
                }

                try
                {
                    TimeEndPeriod(1);
                }
                catch
                {
                }

                _active = false;
            }
        }
    }
}
