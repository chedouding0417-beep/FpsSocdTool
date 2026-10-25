using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FpsSocdTool
{
    internal static class SelfTest
    {
        internal static int Run(string reportPath)
        {
            if (string.IsNullOrEmpty(reportPath))
            {
                reportPath = Path.Combine(Path.GetTempPath(), "FpsSocdTool-selftest.txt");
            }

            List<string> lines = new List<string>();
            int passed = 0;
            int failed = 0;

            Action<bool, string> check = delegate(bool condition, string name)
            {
                if (condition)
                {
                    passed++;
                    lines.Add("[ OK ] " + name);
                }
                else
                {
                    failed++;
                    lines.Add("[FAIL] " + name);
                }
            };

            string tempDirectory = Path.Combine(Path.GetTempPath(),
                "FpsSocdToolSelftest_" + Guid.NewGuid().ToString("N"));
            try
            {
                try
                {
                    Directory.CreateDirectory(tempDirectory);
                }
                catch
                {
                    // Some restricted environments deny the per-user TEMP
                    // folder. Fall back to the application directory.
                    tempDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "selftest-temp-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDirectory);
                }

                RunConfigTests(tempDirectory, check);
                RunHotkeyTests(check);
                RunInputLayoutTests(check);
                RunEngineTests(check);
            }
            catch (Exception exception)
            {
                failed++;
                lines.Add("[FAIL] 自测异常：" + exception.GetType().Name + ": " + exception.Message);
                lines.Add(exception.StackTrace);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                }
                catch
                {
                }
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("FPS SOCD 急停助手 自测报告");
            report.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("运行时：" + Environment.Version);
            report.AppendLine("系统：" + Environment.OSVersion.VersionString);
            report.AppendLine();
            for (int i = 0; i < lines.Count; i++)
            {
                report.AppendLine(lines[i]);
            }

            report.AppendLine();
            report.AppendLine("通过：" + passed + "，失败：" + failed);
            report.AppendLine("RESULT: " + (failed == 0 ? "PASS" : "FAIL"));

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                return 1;
            }

            return failed == 0 ? 0 : 1;
        }

        private static void RunConfigTests(string tempDirectory, Action<bool, string> check)
        {
            string path = Path.Combine(tempDirectory, "config.ini");
            List<string> warnings;
            AppConfig config = AppConfig.Load(path, out warnings);
            check(File.Exists(path), "首次加载会创建默认配置文件");
            check(warnings.Count == 0, "默认配置没有警告");
            check(config.TapMs == 35 && config.CooldownMs == 70, "默认补键参数正确");
            check(config.ToggleHotkey == "Ctrl+Shift+O" && config.PanicHotkey == "F9" &&
                config.FallbackToggleHotkey == string.Empty, "默认热键正确");

            config.TapMs = 42;
            config.CooldownMs = 123;
            config.SkipIfOppositeHeld = false;
            config.UseScanCodes = false;
            config.OnlyInProcess = "game.exe";
            config.ToggleHotkey = "Ctrl+Alt+S";
            config.FallbackToggleHotkey = "Ctrl+Alt+Z";
            config.Save(path);

            AppConfig reloaded = AppConfig.Load(path, out warnings);
            check(reloaded.TapMs == 42 && reloaded.CooldownMs == 123, "数值配置保存并重新加载");
            check(!reloaded.SkipIfOppositeHeld && !reloaded.UseScanCodes, "布尔配置保存并重新加载");
            check(reloaded.OnlyInProcess == "game.exe", "进程过滤配置保存并重新加载");
            check(reloaded.ToggleHotkey == "Ctrl+Alt+S" &&
                reloaded.FallbackToggleHotkey == "Ctrl+Alt+Z", "热键配置保存并重新加载");

            string inlinePath = Path.Combine(tempDirectory, "inline.ini");
            string inlineConfig =
                "[General]\r\n" +
                "TapMs=46 ; 行尾注释\r\n" +
                "CooldownMs=88 # 另一种行尾注释\r\n" +
                "OnlyInProcess=game.exe ; 过滤\r\n" +
                "\r\n" +
                "[Hotkeys]\r\n" +
                "Toggle=Ctrl+Alt+S ; 切换\r\n";
            File.WriteAllText(inlinePath, inlineConfig, new UTF8Encoding(false));
            AppConfig inline = AppConfig.Load(inlinePath, out warnings);
            check(inline.TapMs == 46 && inline.CooldownMs == 88, "支持行尾注释的数值配置");
            check(inline.OnlyInProcess == "game.exe", "支持行尾注释的进程过滤");
            check(inline.ToggleHotkey == "Ctrl+Alt+S", "支持行尾注释的热键");

            string typoPath = Path.Combine(tempDirectory, "typo.ini");
            File.WriteAllText(typoPath,
                "[General]\r\n" +
                "OnlyInProces=game.exe\r\n" +
                "StartEnabeld=true\r\n" +
                "TapMs=35\r\n" +
                "\r\n" +
                "[Hotkeys]\r\n" +
                "Toggle=F8\r\n" +
                "Panic=F9\r\n",
                new UTF8Encoding(false));
            AppConfig typoConfig = AppConfig.Load(typoPath, out warnings);
            bool sawUnknownWarning = false;
            for (int i = 0; i < warnings.Count; i++)
            {
                if (warnings[i].IndexOf("未知配置项", StringComparison.Ordinal) >= 0)
                {
                    sawUnknownWarning = true;
                }
            }

            check(sawUnknownWarning, "未知配置项会给出警告");
            check(typoConfig.OnlyInProcess == string.Empty && !typoConfig.StartEnabled,
                "拼写错误的配置项不会被误用");

            string patchPath = Path.Combine(tempDirectory, "patch.ini");
            File.WriteAllText(patchPath,
                "[General]\r\n" +
                "TapMs=35 ; 手动改过的配置\r\n" +
                "OnlyInProcess=game.exe\r\n" +
                "UnknownOption=abc\r\n" +
                "\r\n" +
                "[Hotkeys]\r\n" +
                "Toggle=Ctrl+Alt+Q\r\n",
                new UTF8Encoding(false));
            AppConfig.SaveTapMs(patchPath, 57);
            string patchedText = File.ReadAllText(patchPath, Encoding.UTF8);
            AppConfig patched = AppConfig.Load(patchPath, out warnings);
            check(patchedText.IndexOf("TapMs=57", StringComparison.Ordinal) >= 0,
                "托盘预设只修改 TapMs");
            check(patchedText.IndexOf("OnlyInProcess=game.exe", StringComparison.Ordinal) >= 0 &&
                patchedText.IndexOf("UnknownOption=abc", StringComparison.Ordinal) >= 0 &&
                patchedText.IndexOf("Toggle=Ctrl+Alt+Q", StringComparison.Ordinal) >= 0,
                "修改 TapMs 不会覆盖其他手动配置");
            check(patched.TapMs == 57 && patched.OnlyInProcess == "game.exe",
                "修改 TapMs 后配置仍可正确加载");
        }

        private static void RunInputLayoutTests(Action<bool, string> check)
        {
            int pointerSize = IntPtr.Size;
            check(Marshal.SizeOf(typeof(INPUT)) == (pointerSize == 8 ? 40 : 28),
                "SendInput 的 INPUT 结构大小正确");
            check(Marshal.SizeOf(typeof(KEYBDINPUT)) == (pointerSize == 8 ? 24 : 16),
                "SendInput 的 KEYBDINPUT 结构大小正确");
            check(Marshal.SizeOf(typeof(MOUSEINPUT)) == (pointerSize == 8 ? 32 : 24),
                "SendInput 的 MOUSEINPUT 结构大小正确");
            check(Marshal.OffsetOf(typeof(KEYBDINPUT), "wVk").ToInt32() == 0 &&
                Marshal.OffsetOf(typeof(KEYBDINPUT), "wScan").ToInt32() == 2 &&
                Marshal.OffsetOf(typeof(KEYBDINPUT), "dwFlags").ToInt32() == 4 &&
                Marshal.OffsetOf(typeof(KEYBDINPUT), "dwExtraInfo").ToInt32() ==
                    (pointerSize == 8 ? 16 : 12), "KEYBDINPUT 字段偏移正确");
            check(Marshal.OffsetOf(typeof(INPUT), "type").ToInt32() == 0 &&
                Marshal.OffsetOf(typeof(INPUT), "U").ToInt32() == (pointerSize == 8 ? 8 : 4),
                "INPUT 的联合体偏移正确");
        }

        private static void RunHotkeyTests(Action<bool, string> check)
        {
            HotkeySpec spec;
            string error;

            check(HotkeyUtil.TryParse("Ctrl+Shift+O", out spec, out error) &&
                spec.VirtualKey == 0x4F &&
                spec.Modifiers == (NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT), "解析 Ctrl+Shift+O");
            check(HotkeyUtil.TryParse("Ctrl+Alt+S", out spec, out error) &&
                spec.VirtualKey == 0x53 &&
                spec.Modifiers == (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT), "解析 Ctrl+Alt+S");
            check(HotkeyUtil.TryParse("Shift+F12", out spec, out error) && spec.VirtualKey == 0x7B,
                "解析 Shift+F12");
            check(!HotkeyUtil.TryParse("A", out spec, out error), "拒绝会把 A 吞掉的单键热键");
            check(!HotkeyUtil.TryParse("Foo", out spec, out error), "拒绝未知按键");
            check(!HotkeyUtil.TryParse("Ctrl+", out spec, out error), "拒绝残缺组合键");
        }

        private static void RunEngineTests(Action<bool, string> check)
        {
            // 1. Releasing A taps D, then releases it.
            FakeEmitter emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                check(emitter.Count("down:D") == 1, "松开 A 后立即按下 D");
                Thread.Sleep(150);
                check(emitter.Count("up:D") == 1, "反向点按 D 后自动松开");
                check(!emitter.IsDown(SocdKey.D) && !engine.HasPendingSyntheticKey,
                    "正常补键结束后没有残留按下状态");
                check(emitter.Count("down:A") == 0, "松开 A 不会误按 A");
            }

            // 2. Releasing D taps A.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.D, true);
                Press(engine, SocdKey.D, false);
                check(emitter.Count("down:A") == 1, "松开 D 后立即按下 A");
                Thread.Sleep(150);
                check(emitter.Count("up:A") == 1, "反向点按 A 后自动松开");
            }

            // 3. Do not tap while the opposite key is physically held.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.D, true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(80);
                check(emitter.Events.Count == 0, "对向键被按住时不重复补键");
            }

            // 4. Disabled engine does nothing.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(false);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(80);
                check(emitter.Events.Count == 0, "暂停状态下不发送按键");
            }

            // 5. Injected events are ignored.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                engine.ProcessKey(SocdKey.A.VirtualKey, SocdKey.A.ScanCode, true, true, IntPtr.Zero);
                engine.ProcessKey(SocdKey.A.VirtualKey, SocdKey.A.ScanCode, false, true, IntPtr.Zero);
                Thread.Sleep(80);
                check(emitter.Events.Count == 0, "忽略模拟注入的按键，避免自触发");
            }

            // 6. Keyboard auto-repeat does not cause repeated taps.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                for (int i = 0; i < 5; i++)
                {
                    Press(engine, SocdKey.A, true);
                }

                Press(engine, SocdKey.A, false);
                Thread.Sleep(150);
                check(emitter.Count("down:D") == 1 && emitter.Count("up:D") == 1,
                    "长按 A 的键盘重复消息不会产生多次补键");
            }

            // 7. Foreground-process filter can veto a tap.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                engine.AllowTrigger = delegate { return false; };
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(80);
                check(emitter.Events.Count == 0, "前台进程过滤生效");

                engine.AllowTrigger = null;
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(80);
                check(emitter.Count("down:D") == 1, "移除过滤后恢复补键");
            }

            // 8. Cooldown prevents machine-gun tapping.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 10, 200);
                engine.SetEnabled(true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(80);
                Press(engine, SocdKey.D, true);
                Press(engine, SocdKey.D, false);
                Thread.Sleep(80);
                check(emitter.Count("down:D") == 1 && emitter.Count("down:A") == 0,
                    "冷却时间内不会连续补键");
                Thread.Sleep(150);
                Press(engine, SocdKey.D, true);
                Press(engine, SocdKey.D, false);
                Thread.Sleep(80);
                check(emitter.Count("down:A") == 1, "冷却结束后恢复补键");
            }

            // 9. Disabling during a tap releases the injected key immediately.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 200, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(30);
                engine.SetEnabled(false);
                check(emitter.Count("down:D") == 1 && emitter.Count("up:D") == 1,
                    "中途暂停会立刻松开已按下的反向键");
                Thread.Sleep(250);
                check(emitter.Count("up:D") == 1, "暂停后不会再补发第二次松开");
            }

            // 10. Scan-code mode detects and injects by scan code only.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, true, 20, 0);
                engine.SetEnabled(true);
                engine.ProcessKey(SocdKey.A.VirtualKey, 0, true, false, IntPtr.Zero);
                engine.ProcessKey(SocdKey.A.VirtualKey, 0, false, false, IntPtr.Zero);
                Thread.Sleep(80);
                check(emitter.Events.Count == 0, "扫描码模式下不会把虚拟键事件误判为 A");

                engine.ProcessKey(0, SocdKey.A.ScanCode, true, false, IntPtr.Zero);
                engine.ProcessKey(0, SocdKey.A.ScanCode, false, false, IntPtr.Zero);
                Thread.Sleep(100);
                check(emitter.Count("down:D") == 1, "扫描码模式下按扫描码识别并补键");
            }

            // 11. Virtual-key mode detects and injects by virtual key only.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                engine.ProcessKey(0, SocdKey.A.ScanCode, true, false, IntPtr.Zero);
                engine.ProcessKey(0, SocdKey.A.ScanCode, false, false, IntPtr.Zero);
                Thread.Sleep(80);
                check(emitter.Events.Count == 0, "虚拟键模式下不会把扫描码事件误判为 A");

                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(100);
                check(emitter.Count("down:D") == 1, "虚拟键模式下按虚拟键识别并补键");
            }

            // 12. A physical press of the tapped key cancels the pending key-up.
            emitter = new FakeEmitter();
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 300, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                check(emitter.Count("down:D") == 1, "补键 D 已按下");
                Thread.Sleep(20);
                Press(engine, SocdKey.D, true);
                check(!engine.HasPendingSyntheticKey, "物理按下 D 后清除待释放状态");
                Thread.Sleep(350);
                check(emitter.Count("up:D") == 0 && !engine.HasPendingSyntheticKey,
                    "物理按下 D 后不再发送提前的自动 key-up");
                Press(engine, SocdKey.D, false);
            }

            // 13. A failed key-up is retried until the synthetic key is released.
            emitter = new FakeEmitter();
            emitter.FailKeyUpCount = 1;
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(120);
                check(emitter.Count("upfail:D") == 1 && engine.HasPendingSyntheticKey,
                    "key-up 失败时保留待释放状态");
                Thread.Sleep(350);
                check(emitter.Count("up:D") == 1 && !engine.HasPendingSyntheticKey &&
                    !emitter.IsDown(SocdKey.D), "key-up 失败后会自动重试并成功松开");
            }

            // 14. A failed key-down does not arm a release timer or leave state behind.
            emitter = new FakeEmitter();
            emitter.FailKeyDown = true;
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 20, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(80);
                check(emitter.Count("down:D") == 0 && emitter.Count("up:D") == 0 &&
                    !engine.HasPendingSyntheticKey, "key-down 失败时不会发送孤儿 key-up");

                emitter.FailKeyDown = false;
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(120);
                check(emitter.Count("down:D") == 1 && emitter.Count("up:D") == 1,
                    "key-down 失败后下一次补键可正常执行");
            }

            // 15. Disable with a failed key-up keeps retrying until released.
            emitter = new FakeEmitter();
            emitter.FailKeyUpCount = 1;
            using (SocdEngine engine = new SocdEngine(emitter))
            {
                ConfigureEngine(engine, false, 300, 0);
                engine.SetEnabled(true);
                Press(engine, SocdKey.A, true);
                Press(engine, SocdKey.A, false);
                Thread.Sleep(30);
                engine.SetEnabled(false);
                check(emitter.Count("upfail:D") == 1 && engine.HasPendingSyntheticKey,
                    "暂停时 key-up 失败仍会保留待释放状态");
                Thread.Sleep(350);
                check(emitter.Count("up:D") == 1 && !engine.HasPendingSyntheticKey &&
                    !emitter.IsDown(SocdKey.D), "暂停后的自动重试最终会松开反向键");
            }

            // 16. Dispose releases a synthetic key that is still down.
            emitter = new FakeEmitter();
            SocdEngine disposedEngine = new SocdEngine(emitter);
            ConfigureEngine(disposedEngine, false, 300, 0);
            disposedEngine.SetEnabled(true);
            Press(disposedEngine, SocdKey.A, true);
            Press(disposedEngine, SocdKey.A, false);
            Thread.Sleep(20);
            disposedEngine.Dispose();
            check(emitter.Count("up:D") == 1 && !emitter.IsDown(SocdKey.D),
                "退出时会尝试松开仍在按下的反向键");
        }

        private static void ConfigureEngine(SocdEngine engine, bool useScanCodes, int tapMs, int cooldownMs)
        {
            engine.UseScanCodes = useScanCodes;
            engine.TapMs = tapMs;
            engine.CooldownMs = cooldownMs;
            engine.SkipIfOppositeHeld = true;
        }

        private static void Press(SocdEngine engine, SocdKey key, bool isDown)
        {
            engine.ProcessKey(key.VirtualKey, key.ScanCode, isDown, false, IntPtr.Zero);
        }

        private sealed class FakeEmitter : IKeyEmitter
        {
            private readonly object _gate = new object();
            private readonly List<string> _events = new List<string>();
            private readonly HashSet<ushort> _downKeys = new HashSet<ushort>();
            private bool _failKeyDown;
            private int _failKeyUpCount;
            private int _lastErrorCode;

            internal List<string> Events
            {
                get
                {
                    lock (_gate)
                    {
                        return new List<string>(_events);
                    }
                }
            }

            internal bool FailKeyDown
            {
                get
                {
                    lock (_gate)
                    {
                        return _failKeyDown;
                    }
                }

                set
                {
                    lock (_gate)
                    {
                        _failKeyDown = value;
                    }
                }
            }

            internal int FailKeyUpCount
            {
                get
                {
                    lock (_gate)
                    {
                        return _failKeyUpCount;
                    }
                }

                set
                {
                    lock (_gate)
                    {
                        _failKeyUpCount = value;
                    }
                }
            }

            public int LastErrorCode
            {
                get
                {
                    lock (_gate)
                    {
                        return _lastErrorCode;
                    }
                }
            }

            public bool KeyDown(SocdKey key)
            {
                lock (_gate)
                {
                    if (_failKeyDown)
                    {
                        _lastErrorCode = 5;
                        _events.Add("downfail:" + Name(key));
                        return false;
                    }

                    _lastErrorCode = 0;
                    _downKeys.Add(key.VirtualKey);
                    _events.Add("down:" + Name(key));
                    return true;
                }
            }

            public bool KeyUp(SocdKey key)
            {
                lock (_gate)
                {
                    if (_failKeyUpCount > 0)
                    {
                        _failKeyUpCount--;
                        _lastErrorCode = 5;
                        _events.Add("upfail:" + Name(key));
                        return false;
                    }

                    _lastErrorCode = 0;
                    _downKeys.Remove(key.VirtualKey);
                    _events.Add("up:" + Name(key));
                    return true;
                }
            }

            internal bool IsDown(SocdKey key)
            {
                lock (_gate)
                {
                    return _downKeys.Contains(key.VirtualKey);
                }
            }

            internal int Count(string entry)
            {
                lock (_gate)
                {
                    int count = 0;
                    for (int i = 0; i < _events.Count; i++)
                    {
                        if (_events[i] == entry)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            private static string Name(SocdKey key)
            {
                return key.VirtualKey == SocdKey.A.VirtualKey ? "A" : "D";
            }
        }
    }
}
