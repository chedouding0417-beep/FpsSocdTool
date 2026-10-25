using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Media;
using System.Threading;
using System.Windows.Forms;

namespace FpsSocdTool
{
    internal sealed class TrayAppContext : ApplicationContext
    {
        private const string Version = "1.7.1";
        private const int HotkeyToggleId = 1;
        private const int HotkeyPanicId = 2;
        private const int HotkeyFallbackToggleId = 3;

        private readonly bool _dryRun;
        private readonly string _baseDirectory;
        private readonly string _configPath;
        private readonly string _logPath;
        private readonly bool _firstRun;

        private AppConfig _config;
        private ProcessFilter _processFilter;
        private SocdEngineGroup _engine;
        private SendInputEmitter _emitter;
        private RawInputKeyboard _rawInput;
        private HotkeyForm _hotkeyWindow;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _toggleItem;
        private ToolStripMenuItem _panicItem;
        private ToolStripMenuItem _tapMsItem;
        private ToolStripMenuItem _cooldownItem;
        private ToolStripMenuItem _openConfigItem;
        private ToolStripMenuItem _reloadItem;
        private ToolStripMenuItem _openLogItem;
        private ToolStripMenuItem _aboutItem;
        private ToolStripMenuItem _exitItem;
        private Icon _iconEnabled;
        private Icon _iconDisabled;
        private System.Windows.Forms.Timer _startupTipTimer;
        private List<string> _configWarnings = new List<string>();
        private bool _toggleHotkeyRegistered;
        private bool _panicHotkeyRegistered;
        private HotkeySpec _toggleHotkeySpec;
        private HotkeySpec _fallbackToggleSpec;
        private HotkeySpec _panicHotkeySpec;
        private bool _toggleHotkeyHeld;
        private bool _fallbackToggleHeld;
        private bool _panicHotkeyHeld;
        private int _lastToggleActionTick;
        private int _lastPanicActionTick;
        private bool _showMenuOnStart;
        private bool _cleanupDone;

        internal TrayAppContext(bool dryRun, string configPath, bool showMenuOnStart)
        {
            _dryRun = dryRun;
            _showMenuOnStart = showMenuOnStart;
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = configPath;
            _logPath = Log.FilePath;
            _firstRun = !File.Exists(_configPath);

            Log.Info("============================================================");
            Log.Info("FPS SOCD 急停助手 v" + Version + " 启动");
            Log.Info("模式：" + (dryRun ? "演练（只写日志，不发送按键）" : "正常"));
            Log.Info("配置：" + _configPath);

            List<string> warnings;
            _config = AppConfig.Load(_configPath, out warnings);
            _configWarnings = warnings;
            for (int i = 0; i < warnings.Count; i++)
            {
                Log.Warn("配置：" + warnings[i]);
            }

            TimerResolution.Begin();

            _processFilter = new ProcessFilter();
            _processFilter.SetList(_config.OnlyInProcess);

            _emitter = new SendInputEmitter();
            _emitter.DryRun = dryRun;
            _emitter.UseScanCodes = _config.UseScanCodes;

            _engine = new SocdEngineGroup(_emitter);
            _engine.UseScanCodes = _config.UseScanCodes;
            _engine.TapMs = _config.TapMs;
            _engine.CooldownMs = _config.CooldownMs;
            _engine.SkipIfOppositeHeld = _config.SkipIfOppositeHeld;
            _engine.AllowTrigger = _processFilter.IsAllowed;
            _engine.TapAction += OnTapAction;
            _engine.SetPhysicalStates(
                NativeMethods.IsVirtualKeyDown(SocdKey.A.VirtualKey),
                NativeMethods.IsVirtualKeyDown(SocdKey.D.VirtualKey),
                NativeMethods.IsVirtualKeyDown(SocdKey.W.VirtualKey),
                NativeMethods.IsVirtualKeyDown(SocdKey.S.VirtualKey));

            BuildMenu();

            _iconEnabled = IconFactory.LoadTrayIcon(Path.Combine(_baseDirectory, "tray-on.ico"));
            _iconDisabled = IconFactory.LoadTrayIcon(Path.Combine(_baseDirectory, "tray-off.ico"));

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = _iconDisabled;
            _trayIcon.Text = "FPS SOCD 急停助手：已暂停";
            _trayIcon.ContextMenuStrip = _menu;
            _trayIcon.DoubleClick += OnTrayDoubleClick;
            // Add the tray icon after the message loop has started (see
            // EnsureTrayIconVisible). This is more reliable with some custom
            // Windows 11 taskbars than adding it inside the constructor.
            _trayIcon.Visible = false;

            _hotkeyWindow = new HotkeyForm();
            _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
            _hotkeyWindow.ShowMenuRequested += OnShowMenuRequested;
            IntPtr hotkeyHandle = _hotkeyWindow.Handle;
            if (hotkeyHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法创建热键消息窗口。");
            }

            _rawInput = new RawInputKeyboard();
            _rawInput.RawKeyEvent += OnRawKeyEvent;
            string rawInputError;
            if (!_rawInput.Install(out rawInputError))
            {
                string message = "无法注册 Raw Input 键盘监听：" + rawInputError;
                Log.Error(message);
                Cleanup();
                throw new InvalidOperationException(message);
            }

            RegisterHotkeys();

            if (_config.StartEnabled)
            {
                SetEnabled(true, false);
            }
            else
            {
                UpdateTrayState();
            }

            _startupTipTimer = new System.Windows.Forms.Timer();
            _startupTipTimer.Interval = _showMenuOnStart ? 500 : 900;
            _startupTipTimer.Tick += OnStartupTipTick;
            _startupTipTimer.Start();
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _statusItem = new ToolStripMenuItem("状态：已暂停");
            _statusItem.Enabled = false;
            _toggleItem = new ToolStripMenuItem("开启");
            _toggleItem.Click += OnToggleClicked;
            _panicItem = new ToolStripMenuItem("紧急停止");
            _panicItem.Click += OnPanicClicked;
            _tapMsItem = new ToolStripMenuItem("反向点按时长");
            _tapMsItem.DropDownOpening += OnTapMsMenuOpening;
            _cooldownItem = new ToolStripMenuItem("反向点按间隔");
            _cooldownItem.DropDownOpening += OnCooldownMenuOpening;
            _openConfigItem = new ToolStripMenuItem("打开配置文件");
            _openConfigItem.Click += OnOpenConfigClicked;
            _reloadItem = new ToolStripMenuItem("重新加载配置");
            _reloadItem.Click += OnReloadClicked;
            _openLogItem = new ToolStripMenuItem("查看日志");
            _openLogItem.Click += OnOpenLogClicked;
            _aboutItem = new ToolStripMenuItem("关于 / 使用说明");
            _aboutItem.Click += OnAboutClicked;
            _exitItem = new ToolStripMenuItem("退出");
            _exitItem.Click += OnExitClicked;

            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_toggleItem);
            _menu.Items.Add(_panicItem);
            _menu.Items.Add(_tapMsItem);
            _menu.Items.Add(_cooldownItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_openConfigItem);
            _menu.Items.Add(_reloadItem);
            _menu.Items.Add(_openLogItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_aboutItem);
            _menu.Items.Add(_exitItem);
            _menu.Opening += OnMenuOpening;
        }

        private void OnStartupTipTick(object sender, EventArgs e)
        {
            if (_startupTipTimer == null)
            {
                return;
            }

            _startupTipTimer.Stop();
            _startupTipTimer.Dispose();
            _startupTipTimer = null;

            EnsureTrayIconVisible();

            if (_showMenuOnStart)
            {
                _showMenuOnStart = false;
                OnShowMenuRequested();
                return;
            }

            if (_config == null || !_config.Notify || _trayIcon == null)
            {
                return;
            }

            string tip;
            ToolTipIcon icon = ToolTipIcon.Info;
            if (_dryRun)
            {
                tip = "演练模式：只写日志，不发送真实按键。按 " + ToggleKeysLabel() + " 可切换。";
            }
            else if (_toggleHotkeySpec == null || _panicHotkeySpec == null)
            {
                string failed = _toggleHotkeySpec == null && _panicHotkeySpec == null
                    ? "开启/暂停、紧急停止热键配置都无法识别"
                    : (_toggleHotkeySpec == null
                        ? "开启/暂停热键 " + _config.ToggleHotkey + " 配置无效"
                        : "紧急停止热键 " + _config.PanicHotkey + " 配置无效");
                tip = failed + "。请再次双击桌面快捷方式打开菜单，或修改 config.ini。";
                icon = ToolTipIcon.Warning;
            }
            else if (_configWarnings != null && _configWarnings.Count > 0)
            {
                tip = "配置文件有 " + _configWarnings.Count +
                    " 个问题，已使用默认值。详情请查看 socd.log 或右键托盘重新加载配置。";
                icon = ToolTipIcon.Warning;
            }
            else if (_firstRun)
            {
                tip = "按 " + ToggleKeysLabel() + " 开启/暂停，按 " + _config.PanicHotkey +
                    " 紧急停止。若看不到托盘图标，双击桌面的“FPS SOCD 控制菜单”快捷方式。";
            }
            else if (_engine != null && _engine.IsEnabled)
            {
                tip = "已按配置自动开启。按 " + ToggleKeysLabel() + " 可暂停。若看不到托盘图标，" +
                    "双击桌面的“FPS SOCD 控制菜单”快捷方式。";
            }
            else
            {
                tip = "工具已运行：按 " + ToggleKeysLabel() + " 开启/暂停，按 " +
                    _config.PanicHotkey + " 紧急停止。若看不到托盘图标，双击桌面的“FPS SOCD 控制菜单”快捷方式。";
            }

            ShowBalloon("FPS SOCD 急停助手", tip, icon);
        }

        private void OnRawKeyEvent(ushort virtualKey, ushort scanCode, bool isDown)
        {
            DebugTrace.Write("Raw 按键 vk=0x" + virtualKey.ToString("X2") +
                " scan=0x" + scanCode.ToString("X2") + " down=" + isDown);

            if (SyntheticInputFilter.ShouldIgnore(virtualKey, scanCode))
            {
                DebugTrace.Write("忽略本工具注入的事件 vk=0x" + virtualKey.ToString("X2") +
                    " scan=0x" + scanCode.ToString("X2") + " down=" + isDown);
                return;
            }

            // Raw Input is also used for the configured hotkeys, so hotkeys work
            // even if the system hotkey registration cannot be delivered.
            HandleRawHotkey(virtualKey, isDown);

            // Cheap pre-filter: the engine only cares about A/D/W/S. Avoid taking
            // its lock for every other key typed system-wide.
            if (virtualKey != SocdKey.A.VirtualKey && virtualKey != SocdKey.D.VirtualKey &&
                virtualKey != SocdKey.W.VirtualKey && virtualKey != SocdKey.S.VirtualKey &&
                scanCode != SocdKey.A.ScanCode && scanCode != SocdKey.D.ScanCode &&
                scanCode != SocdKey.W.ScanCode && scanCode != SocdKey.S.ScanCode)
            {
                return;
            }

            SocdEngineGroup engine = _engine;
            if (engine != null)
            {
                engine.ProcessKey(virtualKey, scanCode, isDown, false, IntPtr.Zero);
            }
        }

        private void HandleRawHotkey(uint virtualKey, bool isDown)
        {
            HotkeySpec toggle = _toggleHotkeySpec;
            HotkeySpec fallback = _fallbackToggleSpec;
            HotkeySpec panic = _panicHotkeySpec;

            if (toggle != null && virtualKey == toggle.VirtualKey)
            {
                if (isDown)
                {
                    if (!_toggleHotkeyHeld)
                    {
                        _toggleHotkeyHeld = true;
                        if (ModifiersMatch(toggle.Modifiers))
                        {
                            _lastToggleActionTick = Environment.TickCount;
                            Log.Info("Raw Input 触发开启/暂停（" + toggle.Text + "）");
                            if (_engine != null)
                            {
                                SetEnabled(!_engine.IsEnabled, true);
                            }
                        }
                        else
                        {
                            Log.Info("Raw Input 收到 " + toggle.Text + "，但修饰键状态不匹配。");
                        }
                    }
                }
                else
                {
                    _toggleHotkeyHeld = false;
                }
            }

            if (fallback != null && virtualKey == fallback.VirtualKey)
            {
                if (isDown)
                {
                    if (!_fallbackToggleHeld)
                    {
                        _fallbackToggleHeld = true;
                        if (ModifiersMatch(fallback.Modifiers))
                        {
                            _lastToggleActionTick = Environment.TickCount;
                            Log.Info("Raw Input 触发开启/暂停（备用热键 " + fallback.Text + "）");
                            if (_engine != null)
                            {
                                SetEnabled(!_engine.IsEnabled, true);
                            }
                        }
                        else
                        {
                            Log.Info("Raw Input 收到备用热键 " + fallback.Text + "，但修饰键状态不匹配。");
                        }
                    }
                }
                else
                {
                    _fallbackToggleHeld = false;
                }
            }

            if (panic != null && virtualKey == panic.VirtualKey)
            {
                if (isDown)
                {
                    if (!_panicHotkeyHeld)
                    {
                        _panicHotkeyHeld = true;
                        if (ModifiersMatch(panic.Modifiers))
                        {
                            _lastPanicActionTick = Environment.TickCount;
                            Log.Info("Raw Input 触发紧急停止（" + panic.Text + "）");
                            if (_engine != null && _engine.IsEnabled)
                            {
                                SetEnabled(false, true);
                            }
                        }
                        else
                        {
                            Log.Info("Raw Input 收到 " + panic.Text + "，但修饰键状态不匹配。");
                        }
                    }
                }
                else
                {
                    _panicHotkeyHeld = false;
                }
            }
        }

        private static bool ModifiersMatch(uint requiredModifiers)
        {
            uint active = 0;
            if (NativeMethods.IsVirtualKeyDown(0x11))
            {
                active |= NativeMethods.MOD_CONTROL;
            }

            if (NativeMethods.IsVirtualKeyDown(0x12))
            {
                active |= NativeMethods.MOD_ALT;
            }

            if (NativeMethods.IsVirtualKeyDown(0x10))
            {
                active |= NativeMethods.MOD_SHIFT;
            }

            if (NativeMethods.IsVirtualKeyDown(0x5B) || NativeMethods.IsVirtualKeyDown(0x5C))
            {
                active |= NativeMethods.MOD_WIN;
            }

            return active == requiredModifiers;
        }

        private void OnTapAction(string activity)
        {
            if (_config != null && _config.LogTaps)
            {
                string line = activity;
                ThreadPool.QueueUserWorkItem(delegate { Log.Info(line); });
            }
        }

        private void EnsureTrayIconVisible()
        {
            try
            {
                if (_trayIcon != null && !_cleanupDone)
                {
                    _trayIcon.Visible = true;
                    UpdateTrayState();
                }
            }
            catch (Exception exception)
            {
                Log.Error("显示托盘图标失败", exception);
            }
        }

        private void OnShowMenuRequested()
        {
            try
            {
                if (_menu != null && !_cleanupDone)
                {
                    EnsureTrayIconVisible();
                    UpdateTrayState();
                    Log.Info("收到显示控制菜单请求");
                    _menu.Show(Cursor.Position);
                }
            }
            catch (Exception exception)
            {
                Log.Error("显示控制菜单失败", exception);
            }
        }

        private void OnHotkeyPressed(int hotkeyId)
        {
            SocdEngineGroup engine = _engine;
            if (engine == null)
            {
                return;
            }

            if (hotkeyId == HotkeyToggleId)
            {
                if (unchecked(Environment.TickCount - _lastToggleActionTick) < 400)
                {
                    return;
                }

                _lastToggleActionTick = Environment.TickCount;
                SetEnabled(!engine.IsEnabled, true);
            }
            else if (hotkeyId == HotkeyFallbackToggleId)
            {
                if (unchecked(Environment.TickCount - _lastToggleActionTick) < 400)
                {
                    return;
                }

                _lastToggleActionTick = Environment.TickCount;
                SetEnabled(!engine.IsEnabled, true);
            }
            else if (hotkeyId == HotkeyPanicId)
            {
                if (unchecked(Environment.TickCount - _lastPanicActionTick) < 400)
                {
                    return;
                }

                _lastPanicActionTick = Environment.TickCount;
                if (engine.IsEnabled)
                {
                    SetEnabled(false, true);
                }
            }
        }

        private void OnToggleClicked(object sender, EventArgs e)
        {
            if (_engine != null)
            {
                SetEnabled(!_engine.IsEnabled, true);
            }
        }

        private void OnPanicClicked(object sender, EventArgs e)
        {
            if (_engine != null && _engine.IsEnabled)
            {
                SetEnabled(false, true);
            }
        }

        private void OnTrayDoubleClick(object sender, EventArgs e)
        {
            if (_engine != null)
            {
                SetEnabled(!_engine.IsEnabled, true);
            }
        }

        private void SetEnabled(bool enabled, bool playFeedback)
        {
            if (_engine == null || _cleanupDone)
            {
                return;
            }

            bool wasEnabled = _engine.IsEnabled;
            if (wasEnabled == enabled)
            {
                return;
            }

            _engine.SetEnabled(enabled);
            UpdateTrayState();
            Log.Info(enabled ? "已开启反向补键" : "已暂停");

            if (!playFeedback)
            {
                return;
            }

            if (_config.Beep)
            {
                try
                {
                    if (enabled)
                    {
                        SystemSounds.Asterisk.Play();
                    }
                    else
                    {
                        SystemSounds.Exclamation.Play();
                    }
                }
                catch
                {
                }
            }

            if (_config.Notify)
            {
                string text = enabled
                    ? "已开启：松开 A 轻点 D，松开 D 轻点 A。"
                    : "已暂停。按 " + ToggleKeysLabel() + " 重新开启。";
                ShowBalloon("FPS SOCD 急停助手", text, ToolTipIcon.Info);
            }
        }

        private void UpdateTrayState()
        {
            if (_trayIcon == null || _engine == null)
            {
                return;
            }

            bool enabled = _engine.IsEnabled;
            _trayIcon.Icon = enabled ? _iconEnabled : _iconDisabled;
            _trayIcon.Text = "FPS SOCD 急停助手：" + (enabled ? "已开启" : "已暂停") +
                (_dryRun ? "（演练）" : string.Empty);

            if (_statusItem != null)
            {
                _statusItem.Text = "状态：" + (enabled ? "已开启" : "已暂停") +
                    (_dryRun ? "（演练模式）" : string.Empty);
                _statusItem.ForeColor = enabled ? Color.FromArgb(0, 128, 64) : SystemColors.ControlText;
            }

            if (_toggleItem != null && _config != null)
            {
                _toggleItem.Text = (enabled ? "暂停" : "开启") + "（" + ToggleKeysLabel() + "）";
                _toggleItem.Checked = enabled;
            }

            if (_panicItem != null && _config != null)
            {
                _panicItem.Text = "紧急停止（" + HotkeyLabel(_config.PanicHotkey, _panicHotkeySpec != null) + "）";
                _panicItem.Enabled = enabled;
            }
        }

        private void OnMenuOpening(object sender, CancelEventArgs e)
        {
            UpdateTrayState();
        }

        private void OnTapMsMenuOpening(object sender, EventArgs e)
        {
            if (_tapMsItem == null || _config == null)
            {
                return;
            }

            _tapMsItem.DropDownItems.Clear();
            int[] presets = new int[] { 1, 2, 3, 4, 5, 10, 15, 20, 25, 30, 35, 40, 50, 60, 80, 100, 150, 200, 250, 300 };
            bool foundCurrent = false;
            for (int i = 0; i < presets.Length; i++)
            {
                int milliseconds = presets[i];
                ToolStripMenuItem item = new ToolStripMenuItem(milliseconds + " ms");
                item.Tag = milliseconds;
                item.Checked = milliseconds == _config.TapMs;
                item.Click += OnTapMsClicked;
                _tapMsItem.DropDownItems.Add(item);
                if (item.Checked)
                {
                    foundCurrent = true;
                }
            }

            if (!foundCurrent)
            {
                ToolStripMenuItem custom = new ToolStripMenuItem("当前：" + _config.TapMs +
                    " ms（可在 config.ini 中修改）");
                custom.Enabled = false;
                _tapMsItem.DropDownItems.Insert(0, custom);
            }

            _tapMsItem.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem customItem = new ToolStripMenuItem("自定义（1-300 ms）...");
            customItem.Click += OnTapMsCustomClicked;
            _tapMsItem.DropDownItems.Add(customItem);
            ToolStripMenuItem hint = new ToolStripMenuItem("数值越大反向移动越明显；FPS 一般先试 1-10 ms");
            hint.Enabled = false;
            _tapMsItem.DropDownItems.Add(hint);
        }

        private void OnTapMsClicked(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null || item.Tag == null || _config == null)
            {
                return;
            }

            ApplyTapMs((int)item.Tag);
        }

        private void OnTapMsCustomClicked(object sender, EventArgs e)
        {
            if (_config == null)
            {
                return;
            }

            using (TapMsDialog dialog = new TapMsDialog(_config.TapMs))
            {
                Form owner = _menu == null ? null : _menu.FindForm();
                DialogResult result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (result == DialogResult.OK)
                {
                    ApplyTapMs(dialog.SelectedValue);
                }
            }
        }

        private void ApplyTapMs(int milliseconds)
        {
            if (_config == null)
            {
                return;
            }

            milliseconds = Math.Max(1, Math.Min(300, milliseconds));
            _config.TapMs = milliseconds;
            if (_engine != null)
            {
                _engine.TapMs = milliseconds;
            }

            try
            {
                if (File.Exists(_configPath))
                {
                    // Patch only TapMs. This preserves any manual config edits
                    // (especially OnlyInProcess) that have not been reloaded yet.
                    AppConfig.SaveTapMs(_configPath, milliseconds);
                }
                else
                {
                    _config.Save(_configPath);
                }

                Log.Info("反向点按时长已调整为 " + milliseconds + " ms");
            }
            catch (Exception exception)
            {
                Log.Error("保存配置失败", exception);
                MessageBox.Show("保存配置失败：\n" + exception.Message, "FPS SOCD 急停助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnCooldownMenuOpening(object sender, EventArgs e)
        {
            if (_cooldownItem == null || _config == null)
            {
                return;
            }

            _cooldownItem.DropDownItems.Clear();
            int[] presets = new int[] { 0, 10, 20, 30, 40, 50, 70, 100, 150, 200, 300, 500, 1000, 2000 };
            bool foundCurrent = false;
            for (int i = 0; i < presets.Length; i++)
            {
                int milliseconds = presets[i];
                ToolStripMenuItem item = new ToolStripMenuItem(milliseconds + " ms");
                item.Tag = milliseconds;
                item.Checked = milliseconds == _config.CooldownMs;
                item.Click += OnCooldownClicked;
                _cooldownItem.DropDownItems.Add(item);
                if (item.Checked)
                {
                    foundCurrent = true;
                }
            }

            if (!foundCurrent)
            {
                ToolStripMenuItem custom = new ToolStripMenuItem("当前：" + _config.CooldownMs +
                    " ms（可在 config.ini 中修改）");
                custom.Enabled = false;
                _cooldownItem.DropDownItems.Insert(0, custom);
            }

            _cooldownItem.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem customItem = new ToolStripMenuItem("自定义（0-2000 ms）...");
            customItem.Click += OnCooldownCustomClicked;
            _cooldownItem.DropDownItems.Add(customItem);
            ToolStripMenuItem hint = new ToolStripMenuItem("两次补键之间的最小间隔；0 = 不限制");
            hint.Enabled = false;
            _cooldownItem.DropDownItems.Add(hint);
        }

        private void OnCooldownClicked(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null || item.Tag == null || _config == null)
            {
                return;
            }

            ApplyCooldownMs((int)item.Tag);
        }

        private void OnCooldownCustomClicked(object sender, EventArgs e)
        {
            if (_config == null)
            {
                return;
            }

            using (CooldownDialog dialog = new CooldownDialog(_config.CooldownMs))
            {
                Form owner = _menu == null ? null : _menu.FindForm();
                DialogResult result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (result == DialogResult.OK)
                {
                    ApplyCooldownMs(dialog.SelectedValue);
                }
            }
        }

        private void ApplyCooldownMs(int milliseconds)
        {
            if (_config == null)
            {
                return;
            }

            milliseconds = Math.Max(0, Math.Min(2000, milliseconds));
            _config.CooldownMs = milliseconds;
            if (_engine != null)
            {
                _engine.CooldownMs = milliseconds;
            }

            try
            {
                if (File.Exists(_configPath))
                {
                    AppConfig.SaveCooldownMs(_configPath, milliseconds);
                }
                else
                {
                    _config.Save(_configPath);
                }

                Log.Info("反向点按间隔已调整为 " + milliseconds + " ms");
            }
            catch (Exception exception)
            {
                Log.Error("保存配置失败", exception);
                MessageBox.Show("保存配置失败：\n" + exception.Message, "FPS SOCD 急停助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnOpenConfigClicked(object sender, EventArgs e)
        {
            OpenWithNotepad(_configPath);
        }

        private void OnOpenLogClicked(object sender, EventArgs e)
        {
            Log.Info("用户打开日志");
            OpenWithNotepad(Log.FilePath);
        }

        private void OpenWithNotepad(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                if (!File.Exists(path))
                {
                    MessageBox.Show("文件不存在：\n" + path, "FPS SOCD 急停助手",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "notepad.exe";
                info.Arguments = "\"" + path + "\"";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception exception)
            {
                Log.Error("打开文件失败", exception);
                MessageBox.Show("打开文件失败：\n" + exception.Message, "FPS SOCD 急停助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnReloadClicked(object sender, EventArgs e)
        {
            try
            {
                AppConfig oldConfig = _config;
                List<string> warnings;
                AppConfig newConfig = AppConfig.Load(_configPath, out warnings);
                for (int i = 0; i < warnings.Count; i++)
                {
                    Log.Warn("重新加载配置：" + warnings[i]);
                }

                bool hotkeysChanged = oldConfig == null ||
                    !string.Equals(oldConfig.ToggleHotkey, newConfig.ToggleHotkey, StringComparison.Ordinal) ||
                    !string.Equals(oldConfig.FallbackToggleHotkey, newConfig.FallbackToggleHotkey, StringComparison.Ordinal) ||
                    !string.Equals(oldConfig.PanicHotkey, newConfig.PanicHotkey, StringComparison.Ordinal);

                _config = newConfig;
                _configWarnings = warnings;
                _emitter.UseScanCodes = newConfig.UseScanCodes;
                _engine.UseScanCodes = newConfig.UseScanCodes;
                _engine.TapMs = newConfig.TapMs;
                _engine.CooldownMs = newConfig.CooldownMs;
                _engine.SkipIfOppositeHeld = newConfig.SkipIfOppositeHeld;
                _processFilter.SetList(newConfig.OnlyInProcess);

                if (hotkeysChanged)
                {
                    // Re-registering a changed hotkey would first unregister the
                    // working one; on failure the user could lose access. Keep the
                    // old hotkeys until the next start instead.
                    _config.ToggleHotkey = oldConfig.ToggleHotkey;
                    _config.FallbackToggleHotkey = oldConfig.FallbackToggleHotkey;
                    _config.PanicHotkey = oldConfig.PanicHotkey;
                    Log.Warn("热键配置已修改，需要重启工具后生效；当前继续使用旧热键。");
                }
                else if (!_toggleHotkeyRegistered || !_panicHotkeyRegistered)
                {
                    RegisterHotkeys();
                }
                else
                {
                    Log.Info("热键未变化，保留当前注册。");
                }

                UpdateTrayState();
                Log.Info("配置已重新加载");

                if (warnings.Count > 0)
                {
                    MessageBox.Show("配置已加载，但以下项目有问题：\n\n" +
                        string.Join("\n", warnings.ToArray()), "FPS SOCD 急停助手",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (hotkeysChanged)
                {
                    ShowBalloon("FPS SOCD 急停助手",
                        "其他配置已生效；新的热键需要重启工具后才会启用。", ToolTipIcon.Warning);
                }
                else if (_config.Notify)
                {
                    ShowBalloon("FPS SOCD 急停助手", "配置已重新加载。", ToolTipIcon.Info);
                }
            }
            catch (Exception exception)
            {
                Log.Error("重新加载配置失败", exception);
                MessageBox.Show("重新加载配置失败：\n" + exception.Message, "FPS SOCD 急停助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnAboutClicked(object sender, EventArgs e)
        {
            string text =
                "FPS SOCD 急停助手 v" + Version + "\n\n" +
                "开启后：\n" +
                "  松开 A → 自动轻点 D\n" +
                "  松开 D → 自动轻点 A\n\n" +
                "热键：" + (_config != null ? ToggleKeysLabel() : "Ctrl+Shift+O") + " 开启/暂停；" +
                (_config != null ? _config.PanicHotkey : "F9") + " 紧急停止。\n" +
                "右键托盘图标可调整反向点按时长、打开配置或退出。\n" +
                "如果托盘图标被 Windows 收进隐藏图标区，再次双击桌面快捷方式也能打开这个控制菜单。\n\n" +
                "配置：\n" + _configPath + "\n" +
                "日志：\n" + _logPath + "\n\n" +
                "说明：本工具通过 Windows Raw Input 读取物理按键，并用 SendInput 模拟普通键盘按键，" +
                "不读取游戏内存、不注入游戏进程、不联网。\n\n" +
                "在线竞技游戏可能禁止宏输入，或由反作弊系统拦截/判定违规。使用前请确认游戏规则，" +
                "风险自负。";
            MessageBox.Show(text, "关于 FPS SOCD 急停助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            Cleanup();
            ExitThread();
        }

        private void RegisterHotkeys()
        {
            if (_hotkeyWindow == null)
            {
                return;
            }

            UnregisterHotkeys();
            _toggleHotkeyRegistered = false;
            _panicHotkeyRegistered = false;
            _toggleHotkeySpec = null;
            _fallbackToggleSpec = null;
            _panicHotkeySpec = null;
            _toggleHotkeyHeld = false;
            _fallbackToggleHeld = false;
            _panicHotkeyHeld = false;

            HotkeySpec toggleSpec;
            string error;
            if (!HotkeyUtil.TryParse(_config.ToggleHotkey, out toggleSpec, out error))
            {
                Log.Warn("开启/暂停热键无效：" + error);
            }
            else
            {
                _toggleHotkeySpec = toggleSpec;
                if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HotkeyToggleId,
                    toggleSpec.Modifiers | NativeMethods.MOD_NOREPEAT, toggleSpec.VirtualKey))
                {
                    int code = Marshal.GetLastWin32Error();
                    Log.Warn("注册开启/暂停热键 \"" + _config.ToggleHotkey + "\" 失败，错误码 " + code +
                        "（" + new Win32Exception(code).Message + "）；将由 Raw Input 兜底处理。");
                }
                else
                {
                    _toggleHotkeyRegistered = true;
                }
            }

            HotkeySpec panicSpec;
            if (!HotkeyUtil.TryParse(_config.PanicHotkey, out panicSpec, out error))
            {
                Log.Warn("紧急停止热键无效：" + error);
            }
            else if (toggleSpec != null &&
                panicSpec.Modifiers == toggleSpec.Modifiers && panicSpec.VirtualKey == toggleSpec.VirtualKey)
            {
                Log.Warn("紧急停止热键与开启/暂停热键相同，已忽略。");
            }
            else
            {
                _panicHotkeySpec = panicSpec;
                if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HotkeyPanicId,
                    panicSpec.Modifiers | NativeMethods.MOD_NOREPEAT, panicSpec.VirtualKey))
                {
                    int code = Marshal.GetLastWin32Error();
                    Log.Warn("注册紧急停止热键 \"" + _config.PanicHotkey + "\" 失败，错误码 " + code +
                        "（" + new Win32Exception(code).Message + "）；将由 Raw Input 兜底处理。");
                }
                else
                {
                    _panicHotkeyRegistered = true;
                }
            }

            if (string.IsNullOrWhiteSpace(_config.FallbackToggleHotkey))
            {
                Log.Info("备用开启/暂停热键未启用。");
            }
            else
            {
                HotkeySpec fallbackSpec;
                if (!HotkeyUtil.TryParse(_config.FallbackToggleHotkey, out fallbackSpec, out error))
                {
                    Log.Warn("备用开启/暂停热键无效：" + error);
                }
                else if ((toggleSpec != null && fallbackSpec.Modifiers == toggleSpec.Modifiers &&
                    fallbackSpec.VirtualKey == toggleSpec.VirtualKey) ||
                    (panicSpec != null && fallbackSpec.Modifiers == panicSpec.Modifiers &&
                    fallbackSpec.VirtualKey == panicSpec.VirtualKey))
                {
                    Log.Warn("备用开启/暂停热键与已配置的主热键或紧急停止热键重复，已忽略。");
                }
                else
                {
                    _fallbackToggleSpec = fallbackSpec;
                    if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HotkeyFallbackToggleId,
                        fallbackSpec.Modifiers | NativeMethods.MOD_NOREPEAT, fallbackSpec.VirtualKey))
                    {
                        int code = Marshal.GetLastWin32Error();
                        Log.Warn("注册备用开启/暂停热键 \"" + _config.FallbackToggleHotkey +
                            "\" 失败，错误码 " + code + "（" + new Win32Exception(code).Message +
                            "）；将由 Raw Input 兜底处理。");
                    }
                }
            }

            Log.Info("热键注册：" + _config.ToggleHotkey + "=" + (_toggleHotkeyRegistered ? "成功" : "失败") +
                "，备用=" + (_fallbackToggleSpec != null ? _fallbackToggleSpec.Text : "未启用") +
                "，" + _config.PanicHotkey + "=" + (_panicHotkeyRegistered ? "成功" : "失败") +
                "（Raw Input 兜底已启用）");
        }

        private void UnregisterHotkeys()
        {
            if (_hotkeyWindow == null)
            {
                return;
            }

            try
            {
                NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyToggleId);
            }
            catch
            {
            }

            try
            {
                NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyPanicId);
            }
            catch
            {
            }

            try
            {
                NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyFallbackToggleId);
            }
            catch
            {
            }
        }

        private static string HotkeyLabel(string configured, bool usable)
        {
            if (!usable)
            {
                return (configured ?? "未设置") + "，不可用";
            }

            return configured;
        }

        private string ToggleKeysLabel()
        {
            string label = HotkeyLabel(_config.ToggleHotkey, _toggleHotkeySpec != null);
            if (_fallbackToggleSpec != null)
            {
                label += " / " + _fallbackToggleSpec.Text;
            }

            return label;
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                if (_trayIcon != null && _trayIcon.Visible)
                {
                    _trayIcon.ShowBalloonTip(2200, title, text, icon);
                }
            }
            catch
            {
            }
        }

        internal void Cleanup()
        {
            if (_cleanupDone)
            {
                return;
            }

            _cleanupDone = true;
            Log.Info("正在退出...");

            try
            {
                if (_startupTipTimer != null)
                {
                    _startupTipTimer.Stop();
                    _startupTipTimer.Dispose();
                    _startupTipTimer = null;
                }
            }
            catch
            {
            }

            try
            {
                if (_engine != null)
                {
                    _engine.SetEnabled(false);
                }
            }
            catch
            {
            }

            try
            {
                if (_rawInput != null)
                {
                    _rawInput.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (_hotkeyWindow != null)
                {
                    UnregisterHotkeys();
                    _hotkeyWindow.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (_menu != null)
                {
                    _menu.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (_iconEnabled != null)
                {
                    _iconEnabled.Dispose();
                }

                if (_iconDisabled != null)
                {
                    _iconDisabled.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (_engine != null)
                {
                    _engine.Dispose();
                }
            }
            catch
            {
            }

            TimerResolution.End();

            Log.Info("已退出");
        }

        protected override void ExitThreadCore()
        {
            Cleanup();
            base.ExitThreadCore();
        }

        private sealed class HotkeyForm : Form
        {
            internal event Action<int> HotkeyPressed;
            internal event Action ShowMenuRequested;

            internal HotkeyForm()
            {
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-32000, -32000, 1, 1);
                Text = NativeMethods.ControlWindowTitle;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == NativeMethods.WM_HOTKEY)
                {
                    Action<int> handler = HotkeyPressed;
                    if (handler != null)
                    {
                        try
                        {
                            handler(message.WParam.ToInt32());
                        }
                        catch (Exception exception)
                        {
                            Log.Error("处理全局热键失败", exception);
                        }
                    }

                    return;
                }

                if (NativeMethods.ShowMenuMessage != 0 &&
                    message.Msg == (int)NativeMethods.ShowMenuMessage)
                {
                    Action handler = ShowMenuRequested;
                    if (handler != null)
                    {
                        try
                        {
                            handler();
                        }
                        catch (Exception exception)
                        {
                            Log.Error("处理显示菜单请求失败", exception);
                        }
                    }

                    return;
                }

                base.WndProc(ref message);
            }
        }
    }
}
