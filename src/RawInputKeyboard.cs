using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FpsSocdTool
{
    /// <summary>
    /// Watches physical keyboard input through Raw Input. The target is a
    /// message-only window with RIDEV_INPUTSINK, so keyboard data is delivered
    /// while another application (e.g. a game) is in the foreground.
    /// </summary>
    internal sealed class RawInputKeyboard : IDisposable
    {
        internal const int WM_INPUT = 0x00FF;

        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEKEYBOARD = 1;
        private const ushort RI_KEY_BREAK = 0x01;
        private static readonly IntPtr HwndMessage = new IntPtr(-3);

        private RawInputWindow _window;
        private bool _installed;

        internal event Action<ushort, ushort, bool> RawKeyEvent;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices(
            [In] RAWINPUTDEVICE[] devices, uint numberOfDevices, uint structureSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRegisteredRawInputDevices(
            IntPtr devices, ref uint numberOfDevices, uint structureSize);

        internal bool Install(out string error)
        {
            error = null;
            try
            {
                _window = new RawInputWindow(this);
            }
            catch (Exception exception)
            {
                error = "创建 Raw Input 消息窗口失败：" + exception.Message;
                Log.Error(error, exception);
                return false;
            }

            RAWINPUTDEVICE device = new RAWINPUTDEVICE();
            device.UsagePage = HID_USAGE_PAGE_GENERIC;
            device.Usage = HID_USAGE_GENERIC_KEYBOARD;
            device.Flags = RIDEV_INPUTSINK;
            device.Target = _window.Handle;

            if (!RegisterRawInputDevices(new RAWINPUTDEVICE[] { device }, 1,
                (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            {
                int code = Marshal.GetLastWin32Error();
                error = "RegisterRawInputDevices 失败，错误码 " + code +
                    "（" + new System.ComponentModel.Win32Exception(code).Message + "）";
                Log.Error("注册 Raw Input 键盘失败：" + error);
                _window.Destroy();
                _window = null;
                return false;
            }

            _installed = true;
            Log.Info("Raw Input 键盘监听已启用（RIDEV_INPUTSINK，message-only window）");
            DebugTrace.Write("RawInput 消息窗口 hwnd=" + _window.Handle +
                " visible=" + _window.IsVisible);

            try
            {
                uint count = 0;
                uint deviceSize = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE));
                GetRegisteredRawInputDevices(IntPtr.Zero, ref count, deviceSize);
                DebugTrace.Write("RawInput 已注册设备数=" + count +
                    " err=" + Marshal.GetLastWin32Error() + " bitness=" + IntPtr.Size * 8);
                if (count > 0)
                {
                    IntPtr buffer = Marshal.AllocHGlobal((int)(count * deviceSize));
                    try
                    {
                        uint filled = count;
                        GetRegisteredRawInputDevices(buffer, ref filled, deviceSize);
                        for (int i = 0; i < filled; i++)
                        {
                            RAWINPUTDEVICE registered = (RAWINPUTDEVICE)Marshal.PtrToStructure(
                                IntPtr.Add(buffer, (int)(i * deviceSize)), typeof(RAWINPUTDEVICE));
                            DebugTrace.Write("RawInput 设备 page=0x" +
                                registered.UsagePage.ToString("X") + " usage=0x" +
                                registered.Usage.ToString("X") + " flags=0x" +
                                registered.Flags.ToString("X") + " target=" + registered.Target);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            catch
            {
            }

            return true;
        }

        private void RaiseKey(ushort virtualKey, ushort scanCode, bool isDown)
        {
            Action<ushort, ushort, bool> handler = RawKeyEvent;
            if (handler != null)
            {
                handler(virtualKey, scanCode, isDown);
            }
        }

        /// <summary>Parses one WM_INPUT message into a keyboard event.</summary>
        internal static bool TryRead(IntPtr rawInputHandle, out ushort virtualKey,
            out ushort scanCode, out bool isDown)
        {
            virtualKey = 0;
            scanCode = 0;
            isDown = false;

            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(rawInputHandle, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0)
            {
                return false;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint actual = GetRawInputData(rawInputHandle, RID_INPUT, buffer, ref size, headerSize);
                if (actual != size)
                {
                    return false;
                }

                RAWINPUTHEADER header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer,
                    typeof(RAWINPUTHEADER));
                if (header.Type != RIM_TYPEKEYBOARD)
                {
                    return false;
                }

                int headerBytes = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                int keyboardBytes = Marshal.SizeOf(typeof(RAWKEYBOARD));
                if (size < (uint)(headerBytes + keyboardBytes))
                {
                    return false;
                }

                IntPtr keyboardPointer = IntPtr.Add(buffer, headerBytes);
                RAWKEYBOARD keyboard = (RAWKEYBOARD)Marshal.PtrToStructure(keyboardPointer,
                    typeof(RAWKEYBOARD));

                // 0xFF is used by the OS for some synthetic/fake keys.
                if (keyboard.VKey == 0xFF)
                {
                    return false;
                }

                virtualKey = keyboard.VKey;
                scanCode = keyboard.MakeCode;
                isDown = (keyboard.Flags & RI_KEY_BREAK) == 0;
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("解析 Raw Input 键盘数据失败", exception);
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            if (_installed)
            {
                try
                {
                    RAWINPUTDEVICE device = new RAWINPUTDEVICE();
                    device.UsagePage = HID_USAGE_PAGE_GENERIC;
                    device.Usage = HID_USAGE_GENERIC_KEYBOARD;
                    device.Flags = RIDEV_REMOVE;
                    device.Target = IntPtr.Zero;
                    RegisterRawInputDevices(new RAWINPUTDEVICE[] { device }, 1,
                        (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
                }
                catch
                {
                }

                _installed = false;
            }

            if (_window != null)
            {
                _window.Destroy();
                _window = null;
            }
        }

        private sealed class RawInputWindow : NativeWindow
        {
            private readonly RawInputKeyboard _owner;

            internal RawInputWindow(RawInputKeyboard owner)
            {
                _owner = owner;
                CreateParams parameters = new CreateParams();
                parameters.Caption = string.Empty;
                parameters.ClassName = "STATIC";
                parameters.Parent = HwndMessage;
                parameters.Style = 0;
                parameters.X = 0;
                parameters.Y = 0;
                parameters.Width = 0;
                parameters.Height = 0;
                CreateHandle(parameters);
                if (Handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("CreateHandle 返回空句柄。");
                }
            }

            internal bool IsVisible
            {
                get { return IsWindowVisible(Handle); }
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WM_INPUT)
                {
                    DebugTrace.Write("RawInputWindow 收到 WM_INPUT");
                    ushort virtualKey;
                    ushort scanCode;
                    bool isDown;
                    if (TryRead(message.LParam, out virtualKey, out scanCode, out isDown))
                    {
                        _owner.RaiseKey(virtualKey, scanCode, isDown);
                    }

                    base.WndProc(ref message);
                    return;
                }

                base.WndProc(ref message);
            }

            internal void Destroy()
            {
                IntPtr handle = Handle;
                if (handle != IntPtr.Zero)
                {
                    ReleaseHandle();
                    DestroyWindow(handle);
                }
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool DestroyWindow(IntPtr hWnd);
        }
    }
}
