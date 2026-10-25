using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FpsSocdTool
{
    /// <summary>
    /// Raw Input reports injected SendInput events too. Ignore the tap that this
    /// tool just sent so it cannot feed back into the physical-key state machine.
    /// A/D and W/S can be injected at the same time, so the filter tracks every
    /// pending key independently.
    /// </summary>
    internal static class SyntheticInputFilter
    {
        private sealed class PendingKey
        {
            internal ushort VirtualKey;
            internal ushort ScanCode;
            internal long UntilTimestamp;
        }

        private static readonly object Gate = new object();
        private static readonly List<PendingKey> Pending = new List<PendingKey>();
        // Only needs to cover the raw-input echo of one SendInput event. Keep it
        // short so a real key press immediately after a tap is not swallowed.
        private static readonly long WindowTicks = (long)(Stopwatch.Frequency * 0.030);

        internal static void Mark(SocdKey key)
        {
            lock (Gate)
            {
                long now = Stopwatch.GetTimestamp();
                Prune(now);

                for (int i = 0; i < Pending.Count; i++)
                {
                    if (Pending[i].VirtualKey == key.VirtualKey ||
                        Pending[i].ScanCode == key.ScanCode)
                    {
                        Pending[i].UntilTimestamp = now + WindowTicks;
                        return;
                    }
                }

                PendingKey entry = new PendingKey();
                entry.VirtualKey = key.VirtualKey;
                entry.ScanCode = key.ScanCode;
                entry.UntilTimestamp = now + WindowTicks;
                Pending.Add(entry);
            }
        }

        internal static bool ShouldIgnore(ushort virtualKey, ushort scanCode)
        {
            lock (Gate)
            {
                long now = Stopwatch.GetTimestamp();
                Prune(now);

                for (int i = 0; i < Pending.Count; i++)
                {
                    PendingKey entry = Pending[i];
                    if (entry.VirtualKey == virtualKey || entry.ScanCode == scanCode)
                    {
                        // Consume one echo, so a later real press is not ignored.
                        Pending.RemoveAt(i);
                        return true;
                    }
                }

                return false;
            }
        }

        private static void Prune(long now)
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (now >= Pending[i].UntilTimestamp)
                {
                    Pending.RemoveAt(i);
                }
            }
        }
    }
}
