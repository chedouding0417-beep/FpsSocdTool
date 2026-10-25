using System;

namespace FpsSocdTool
{
    internal sealed class HotkeySpec
    {
        internal uint Modifiers;
        internal uint VirtualKey;
        internal string Text;
    }

    internal static class HotkeyUtil
    {
        internal static bool TryParse(string text, out HotkeySpec spec, out string error)
        {
            spec = null;
            error = null;

            if (text == null || text.Trim().Length == 0)
            {
                error = "热键不能为空";
                return false;
            }

            string trimmed = text.Trim();
            string[] rawParts = trimmed.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (rawParts.Length == 0)
            {
                error = "热键格式无效";
                return false;
            }

            uint modifiers = 0;
            uint virtualKey = 0;

            for (int i = 0; i < rawParts.Length; i++)
            {
                string part = rawParts[i].Trim();
                if (part.Length == 0)
                {
                    error = "热键格式无效";
                    return false;
                }

                if (i < rawParts.Length - 1)
                {
                    uint modifier;
                    if (!TryParseModifier(part, out modifier))
                    {
                        error = "无法识别的修饰键：" + part;
                        return false;
                    }

                    modifiers |= modifier;
                }
                else if (!TryParseKey(part, out virtualKey))
                {
                    error = "无法识别的按键：" + part;
                    return false;
                }
            }

            if (virtualKey == 0)
            {
                error = "热键格式无效";
                return false;
            }

            // A bare A/D hotkey would swallow the movement key itself.
            if (modifiers == 0 && (virtualKey == 0x41 || virtualKey == 0x44))
            {
                error = "不能把单独的 A 或 D 设为热键，请加 Ctrl/Alt/Shift 等修饰键";
                return false;
            }

            spec = new HotkeySpec();
            spec.Modifiers = modifiers;
            spec.VirtualKey = virtualKey;
            spec.Text = trimmed;
            return true;
        }

        private static bool TryParseModifier(string text, out uint modifier)
        {
            modifier = 0;
            switch (text.Trim().ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifier = NativeMethods.MOD_CONTROL;
                    return true;
                case "ALT":
                    modifier = NativeMethods.MOD_ALT;
                    return true;
                case "SHIFT":
                    modifier = NativeMethods.MOD_SHIFT;
                    return true;
                case "WIN":
                case "WINDOWS":
                case "META":
                    modifier = NativeMethods.MOD_WIN;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseKey(string text, out uint virtualKey)
        {
            virtualKey = 0;
            string key = text.Trim().ToUpperInvariant();
            if (key.Length == 0)
            {
                return false;
            }

            if (key.Length == 1)
            {
                char character = key[0];
                if ((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9'))
                {
                    virtualKey = (uint)character;
                    return true;
                }
            }

            if (key.Length >= 2 && key[0] == 'F')
            {
                int number;
                if (int.TryParse(key.Substring(1), out number) && number >= 1 && number <= 24)
                {
                    virtualKey = (uint)(0x70 + number - 1);
                    return true;
                }
            }

            switch (key)
            {
                case "SPACE":
                    virtualKey = 0x20;
                    return true;
                case "TAB":
                    virtualKey = 0x09;
                    return true;
                case "ENTER":
                case "RETURN":
                    virtualKey = 0x0D;
                    return true;
                case "ESC":
                case "ESCAPE":
                    virtualKey = 0x1B;
                    return true;
                case "BACKSPACE":
                    virtualKey = 0x08;
                    return true;
                case "INSERT":
                    virtualKey = 0x2D;
                    return true;
                case "DELETE":
                case "DEL":
                    virtualKey = 0x2E;
                    return true;
                case "HOME":
                    virtualKey = 0x24;
                    return true;
                case "END":
                    virtualKey = 0x23;
                    return true;
                case "PAGEUP":
                case "PGUP":
                    virtualKey = 0x21;
                    return true;
                case "PAGEDOWN":
                case "PGDN":
                    virtualKey = 0x22;
                    return true;
                case "UP":
                    virtualKey = 0x26;
                    return true;
                case "DOWN":
                    virtualKey = 0x28;
                    return true;
                case "LEFT":
                    virtualKey = 0x25;
                    return true;
                case "RIGHT":
                    virtualKey = 0x27;
                    return true;
                default:
                    return false;
            }
        }
    }
}
