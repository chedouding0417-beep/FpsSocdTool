using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FpsSocdTool
{
    internal sealed class AppConfig
    {
        internal bool StartEnabled;
        internal int TapMs = 35;
        internal int CooldownMs = 70;
        internal bool SkipIfOppositeHeld = true;
        internal bool UseScanCodes = true;
        internal string OnlyInProcess = string.Empty;
        internal bool Beep = true;
        internal bool Notify = true;
        internal bool LogTaps;
        internal string ToggleHotkey = "Ctrl+Shift+O";
        internal string FallbackToggleHotkey = string.Empty;
        internal string PanicHotkey = "F9";

        internal static AppConfig Load(string path, out List<string> warnings)
        {
            List<string> localWarnings = new List<string>();
            AppConfig config = new AppConfig();

            if (!File.Exists(path))
            {
                try
                {
                    config.Save(path);
                }
                catch (Exception exception)
                {
                    localWarnings.Add("无法创建默认配置文件：" + exception.Message);
                }

                warnings = localWarnings;
                return config;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                localWarnings.Add("读取配置失败，已使用默认值：" + exception.Message);
                warnings = localWarnings;
                return config;
            }

            string section = string.Empty;
            int keyLineCount = 0;
            int knownKeyCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) ||
                    line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, equalsIndex).Trim().ToLowerInvariant();
                string value = StripInlineComment(line.Substring(equalsIndex + 1)).Trim();
                string fullKey = section + "." + key;
                keyLineCount++;
                bool recognized = true;

                switch (fullKey)
                {
                    case "general.startenabled":
                        config.StartEnabled = ParseBool(value, config.StartEnabled, fullKey, localWarnings);
                        break;
                    case "general.tapms":
                        config.TapMs = ParseInt(value, config.TapMs, 1, 300, fullKey, localWarnings);
                        break;
                    case "general.cooldownms":
                        config.CooldownMs = ParseInt(value, config.CooldownMs, 0, 2000, fullKey, localWarnings);
                        break;
                    case "general.skipifoppositeheld":
                        config.SkipIfOppositeHeld = ParseBool(value, config.SkipIfOppositeHeld, fullKey, localWarnings);
                        break;
                    case "general.usescancodes":
                        config.UseScanCodes = ParseBool(value, config.UseScanCodes, fullKey, localWarnings);
                        break;
                    case "general.onlyinprocess":
                        config.OnlyInProcess = value;
                        break;
                    case "general.beep":
                        config.Beep = ParseBool(value, config.Beep, fullKey, localWarnings);
                        break;
                    case "general.notify":
                        config.Notify = ParseBool(value, config.Notify, fullKey, localWarnings);
                        break;
                    case "general.logtaps":
                        config.LogTaps = ParseBool(value, config.LogTaps, fullKey, localWarnings);
                        break;
                    case "hotkeys.toggle":
                        if (value.Length == 0)
                        {
                            localWarnings.Add("热键 Toggle 为空，已使用默认值 Ctrl+Shift+O");
                        }
                        else
                        {
                            config.ToggleHotkey = value;
                        }

                        break;
                    case "hotkeys.fallbacktoggle":
                        config.FallbackToggleHotkey = value;
                        break;
                    case "hotkeys.panic":
                        if (value.Length == 0)
                        {
                            localWarnings.Add("热键 Panic 为空，已使用默认值 F9");
                        }
                        else
                        {
                            config.PanicHotkey = value;
                        }

                        break;
                    default:
                        recognized = false;
                        break;
                }

                if (recognized)
                {
                    knownKeyCount++;
                }
                else
                {
                    localWarnings.Add("未知配置项：[" + section + "] " + key +
                        "（拼写错误或放错了段？该行已被忽略）");
                }
            }

            if (keyLineCount > 0 && knownKeyCount == 0)
            {
                localWarnings.Add("配置文件中没有识别到任何有效配置项，请检查 [General] / [Hotkeys] 段名是否正确。");
            }

            warnings = localWarnings;
            return config;
        }

        internal void Save(string path)
        {
            WriteAllTextAtomic(path, BuildText());
        }

        /// <summary>
        /// Updates only the TapMs line, preserving every other line, comment and
        /// unknown key in the user's config file.
        /// </summary>
        internal static void SaveTapMs(string path, int tapMs)
        {
            List<string> lines = new List<string>();
            if (File.Exists(path))
            {
                lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));
            }

            int generalStart = -1;
            int generalEnd = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string header = lines[i].Trim();
                if (!(header.StartsWith("[", StringComparison.Ordinal) && header.EndsWith("]", StringComparison.Ordinal)))
                {
                    continue;
                }

                if (generalStart >= 0)
                {
                    generalEnd = i;
                    break;
                }

                string sectionName = header.Substring(1, header.Length - 2).Trim();
                if (sectionName.Equals("General", StringComparison.OrdinalIgnoreCase))
                {
                    generalStart = i;
                }
            }

            if (generalStart < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add("[General]");
                lines.Add("TapMs=" + tapMs.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                bool updated = false;
                for (int i = generalStart + 1; i < generalEnd; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) ||
                        line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, equalsIndex).Trim();
                    if (key.Equals("TapMs", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "TapMs=" + tapMs.ToString(CultureInfo.InvariantCulture);
                        updated = true;
                        break;
                    }
                }

                if (!updated)
                {
                    lines.Insert(generalStart + 1, "TapMs=" + tapMs.ToString(CultureInfo.InvariantCulture));
                }
            }

            WriteAllTextAtomic(path, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine);
        }

        private static void WriteAllTextAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content, new UTF8Encoding(false));

            try
            {
                if (File.Exists(path))
                {
                    string backupPath = path + ".bak";
                    bool replaced = false;
                    try
                    {
                        File.Replace(tempPath, path, backupPath, true);
                        replaced = true;
                    }
                    catch
                    {
                    }

                    if (replaced)
                    {
                        try
                        {
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        File.Copy(tempPath, path, true);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static string StripInlineComment(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            for (int i = 1; i < value.Length; i++)
            {
                char character = value[i];
                if ((character == ';' || character == '#') && char.IsWhiteSpace(value[i - 1]))
                {
                    return value.Substring(0, i).TrimEnd();
                }
            }

            return value;
        }

        private static bool ParseBool(string value, bool fallback, string key, List<string> warnings)
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "true" || normalized == "1" || normalized == "yes" || normalized == "on")
            {
                return true;
            }

            if (normalized == "false" || normalized == "0" || normalized == "no" || normalized == "off")
            {
                return false;
            }

            warnings.Add("配置项 " + key + " 的值 \"" + value + "\" 无效，已使用默认值 " + (fallback ? "true" : "false"));
            return fallback;
        }

        private static int ParseInt(string value, int fallback, int minimum, int maximum, string key, List<string> warnings)
        {
            int number;
            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                if (number < minimum)
                {
                    warnings.Add("配置项 " + key + " 小于 " + minimum + "，已按最小值处理");
                    return minimum;
                }

                if (number > maximum)
                {
                    warnings.Add("配置项 " + key + " 大于 " + maximum + "，已按最大值处理");
                    return maximum;
                }

                return number;
            }

            warnings.Add("配置项 " + key + " 的值 \"" + value + "\" 不是整数，已使用默认值 " + fallback);
            return fallback;
        }

        private string BuildText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("; ============================================================");
            builder.AppendLine(";  FPS SOCD 急停助手 配置文件");
            builder.AppendLine(";  修改后可在托盘菜单选择“重新加载配置”，或重启工具。");
            builder.AppendLine("; ============================================================");
            builder.AppendLine();
            builder.AppendLine("[General]");

            builder.AppendLine("; 启动工具后是否立即开启反向补键（true/false）");
            builder.AppendLine("StartEnabled=" + ToBoolText(StartEnabled));
            builder.AppendLine();
            builder.AppendLine("; 松开 A/D 或 W/S 后，向反方向轻点的持续时间（1-300 毫秒）");
            builder.AppendLine("; 越大反向移动越明显，FPS 可先试 1-10");
            builder.AppendLine("TapMs=" + TapMs.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("; 两次补键之间的最小间隔（0-2000 毫秒），防止连点抖动");
            builder.AppendLine("CooldownMs=" + CooldownMs.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("; 另一方向键正被按住时，是否跳过补键");
            builder.AppendLine("SkipIfOppositeHeld=" + ToBoolText(SkipIfOppositeHeld));
            builder.AppendLine();
            builder.AppendLine("; 发送扫描码（true）或虚拟键（false）");
            builder.AppendLine("; 游戏不识别时可以把 true 改成 false 再试");
            builder.AppendLine("UseScanCodes=" + ToBoolText(UseScanCodes));
            builder.AppendLine();
            builder.AppendLine("; 仅在指定进程为前台时生效，逗号分隔；留空 = 所有程序");
            builder.AppendLine("; 例如：OnlyInProcess=game.exe");
            builder.AppendLine("OnlyInProcess=" + (OnlyInProcess ?? string.Empty));
            builder.AppendLine();
            builder.AppendLine("; 切换开关时是否播放提示音 / 显示托盘气泡");
            builder.AppendLine("Beep=" + ToBoolText(Beep));
            builder.AppendLine("Notify=" + ToBoolText(Notify));
            builder.AppendLine();
            builder.AppendLine("; 是否把每次补键写入 socd.log（调试用，平时建议 false）");
            builder.AppendLine("LogTaps=" + ToBoolText(LogTaps));
            builder.AppendLine();
            builder.AppendLine("[Hotkeys]");
            builder.AppendLine("; 开启/暂停开关；支持 F1-F24、A-Z、0-9 及 Ctrl/Alt/Shift/Win 组合");
            builder.AppendLine("; 例如 Ctrl+Shift+O、Ctrl+Alt+S");
            builder.AppendLine("Toggle=" + (ToggleHotkey ?? "Ctrl+Shift+O"));
            builder.AppendLine();
            builder.AppendLine("; 可选备用开启/暂停热键；留空=关闭");
            builder.AppendLine("FallbackToggle=" + (FallbackToggleHotkey ?? string.Empty));
            builder.AppendLine();
            builder.AppendLine("; 紧急停止（强制暂停）");
            builder.AppendLine("Panic=" + (PanicHotkey ?? "F9"));
            return builder.ToString();
        }

        private static string ToBoolText(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
