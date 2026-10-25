using System;
using System.Runtime.InteropServices;

namespace FpsSocdTool
{
    internal struct SocdKey
    {
        internal static readonly SocdKey A = new SocdKey(0x41, 0x1E);
        internal static readonly SocdKey D = new SocdKey(0x44, 0x20);
        internal static readonly SocdKey W = new SocdKey(0x57, 0x11);
        internal static readonly SocdKey S = new SocdKey(0x53, 0x1F);

        public readonly ushort VirtualKey;
        public readonly ushort ScanCode;

        public SocdKey(ushort virtualKey, ushort scanCode)
        {
            VirtualKey = virtualKey;
            ScanCode = scanCode;
        }
    }

    internal interface IKeyEmitter
    {
        int LastErrorCode { get; }
        bool KeyDown(SocdKey key);
        bool KeyUp(SocdKey key);
    }

    internal sealed class SendInputEmitter : IKeyEmitter
    {
        internal volatile bool UseScanCodes = true;
        internal volatile bool DryRun;

        private bool _keyDownUseScanCodes = true;
        private int _lastErrorCode;

        public int LastErrorCode
        {
            get { return _lastErrorCode; }
        }

        public bool KeyDown(SocdKey key)
        {
            // Remember the identity mode used for the press so a later release
            // still targets the same key even if the config is reloaded midway.
            _keyDownUseScanCodes = UseScanCodes;
            return Send(key, true, _keyDownUseScanCodes);
        }

        public bool KeyUp(SocdKey key)
        {
            return Send(key, false, _keyDownUseScanCodes);
        }

        private bool Send(SocdKey key, bool down, bool useScanCodes)
        {
            _lastErrorCode = 0;
            string name = GetKeyName(key);
            if (DryRun)
            {
                Log.Info("[演练] " + (down ? "按下 " : "松开 ") + name);
                return true;
            }

            INPUT input = new INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.U.ki.wVk = useScanCodes ? (ushort)0 : key.VirtualKey;
            input.U.ki.wScan = useScanCodes ? key.ScanCode : (ushort)0;
            input.U.ki.dwFlags = useScanCodes ? NativeMethods.KEYEVENTF_SCANCODE : 0;
            if (!down)
            {
                input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_KEYUP;
            }

            input.U.ki.time = 0;
            input.U.ki.dwExtraInfo = NativeMethods.MagicExtraInfo;

            INPUT[] inputs = new INPUT[] { input };
            SyntheticInputFilter.Mark(key);
            uint sent = NativeMethods.SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            DebugTrace.Write("SendInput " + (down ? "DOWN " : "UP ") + name +
                " scan=" + useScanCodes + " sent=" + sent +
                " err=" + Marshal.GetLastWin32Error());
            if (sent != 1)
            {
                _lastErrorCode = Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }

        private static string GetKeyName(SocdKey key)
        {
            if (key.VirtualKey == SocdKey.A.VirtualKey)
            {
                return "A";
            }

            if (key.VirtualKey == SocdKey.D.VirtualKey)
            {
                return "D";
            }

            return string.Format("VK 0x{0:X2}", key.VirtualKey);
        }
    }
}
