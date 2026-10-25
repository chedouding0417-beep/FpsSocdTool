using System;
using System.Diagnostics;
using System.Threading;

namespace FpsSocdTool
{
    /// <summary>
    /// Platform-independent SOCD state machine. It watches physical A/D releases
    /// and asks the key emitter for one short tap in the opposite direction.
    /// </summary>
    internal sealed class SocdEngine : IDisposable
    {
        private const int RetryReleaseMs = 250;

        private readonly object _gate = new object();
        private readonly IKeyEmitter _emitter;
        private readonly Timer _releaseTimer;
        private readonly SocdKey _negativeKey;
        private readonly SocdKey _positiveKey;

        private bool _enabled;
        private bool _disposed;
        private bool _negativeHeld;
        private bool _positiveHeld;
        private bool _tapActive;
        private SocdKey _tapKey;
        private bool _syntheticDown;
        private SocdKey _syntheticKey;
        private int _releaseFailureCount;
        private long _lastTapTimestamp;
        private int _tapMs = 35;
        private int _cooldownMs = 70;
        private int _activationDelayMs;
        private bool _skipIfOppositeHeld = true;
        private bool _activationPending;
        private SocdKey _activationKey;
        private string _activationTrigger;
        private int _activationGeneration;
        private Timer _activationTimer;

        internal SocdEngine(IKeyEmitter emitter)
            : this(emitter, SocdKey.A, SocdKey.D)
        {
        }

        internal SocdEngine(IKeyEmitter emitter, SocdKey negativeKey, SocdKey positiveKey)
        {
            if (emitter == null)
            {
                throw new ArgumentNullException("emitter");
            }

            if (negativeKey.VirtualKey == positiveKey.VirtualKey ||
                negativeKey.ScanCode == positiveKey.ScanCode)
            {
                throw new ArgumentException("SOCD 对的两颗键不能相同。");
            }

            _emitter = emitter;
            _negativeKey = negativeKey;
            _positiveKey = positiveKey;
            _releaseTimer = new Timer(OnReleaseTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Optional foreground-process filter. Returning false skips the tap.</summary>
        internal Func<bool> AllowTrigger { get; set; }

        internal event Action<string> TapAction;

        /// <summary>If true, both detection and injection use set-1 scan codes.</summary>
        internal bool UseScanCodes { get; set; }

        internal int TapMs
        {
            get { return _tapMs; }
            set { _tapMs = Clamp(value, 1, 300); }
        }

        internal int CooldownMs
        {
            get { return _cooldownMs; }
            set { _cooldownMs = Clamp(value, 0, 2000); }
        }

        /// <summary>Delay between the physical release and the reverse tap.</summary>
        internal int ActivationDelayMs
        {
            get { return _activationDelayMs; }
            set
            {
                _activationDelayMs = Clamp(value, 0, 500);
                lock (_gate)
                {
                    if (_activationPending)
                    {
                        CancelPendingActivationLocked();
                    }
                }
            }
        }

        internal bool SkipIfOppositeHeld
        {
            get { return _skipIfOppositeHeld; }
            set { _skipIfOppositeHeld = value; }
        }

        internal bool IsEnabled
        {
            get
            {
                lock (_gate)
                {
                    return _enabled;
                }
            }
        }

        /// <summary>Test/diagnostic hook: is a synthetic key still believed to be down?</summary>
        internal bool HasPendingSyntheticKey
        {
            get
            {
                lock (_gate)
                {
                    return _syntheticDown;
                }
            }
        }

        internal void SetPhysicalStates(bool negativeDown, bool positiveDown)
        {
            lock (_gate)
            {
                _negativeHeld = negativeDown;
                _positiveHeld = positiveDown;
            }
        }

        internal void SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                if (_disposed || _enabled == enabled)
                {
                    return;
                }

                _enabled = enabled;
                if (!enabled)
                {
                    CancelPendingActivationLocked();

                    if (_tapActive)
                    {
                        _tapActive = false;
                        _tapKey = default(SocdKey);
                        CancelTimerLocked();
                    }

                    if (_syntheticDown)
                    {
                        ReleaseSyntheticLocked(true);
                    }
                }
            }
        }

        internal void ProcessKey(uint virtualKey, uint scanCode, bool isDown, bool injected, IntPtr extraInfo)
        {
            // Ignore every synthetic key, including our own opposite tap, so the
            // state machine only follows real hardware input.
            if (injected || extraInfo == NativeMethods.MagicExtraInfo)
            {
                return;
            }

            string activity;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                // Detection and injection must use the same key identity.
                bool useScanCodes = UseScanCodes;
                bool isNegative = useScanCodes
                    ? scanCode == _negativeKey.ScanCode
                    : virtualKey == _negativeKey.VirtualKey;
                bool isPositive = useScanCodes
                    ? scanCode == _positiveKey.ScanCode
                    : virtualKey == _positiveKey.VirtualKey;
                if (!isNegative && !isPositive)
                {
                    return;
                }

                DebugTrace.Write("引擎收到 " + (isNegative ? KeyName(_negativeKey) : KeyName(_positiveKey)) +
                    " down=" + isDown + " enabled=" + _enabled +
                    " negativeHeld=" + _negativeHeld + " positiveHeld=" + _positiveHeld +
                    " tapActive=" + _tapActive);

                if (isNegative)
                {
                    if (isDown)
                    {
                        _negativeHeld = true;
                        CancelPendingActivationLocked();
                        CancelTapForPhysicalKeyLocked(_negativeKey);
                        return;
                    }

                    _negativeHeld = false;
                    if (!_enabled)
                    {
                        DebugTrace.Write("引擎：" + KeyName(_negativeKey) + " 松开，但当前未开启");
                        return;
                    }

                    if (_skipIfOppositeHeld && _positiveHeld)
                    {
                        DebugTrace.Write("引擎：" + KeyName(_negativeKey) + " 松开，但 " +
                            KeyName(_positiveKey) + " 仍按住，跳过");
                        return;
                    }

                    activity = StartOrScheduleTapLocked(_positiveKey, "松开 " + KeyName(_negativeKey));
                }
                else
                {
                    if (isDown)
                    {
                        _positiveHeld = true;
                        CancelPendingActivationLocked();
                        CancelTapForPhysicalKeyLocked(_positiveKey);
                        return;
                    }

                    _positiveHeld = false;
                    if (!_enabled)
                    {
                        DebugTrace.Write("引擎：" + KeyName(_positiveKey) + " 松开，但当前未开启");
                        return;
                    }

                    if (_skipIfOppositeHeld && _negativeHeld)
                    {
                        DebugTrace.Write("引擎：" + KeyName(_positiveKey) + " 松开，但 " +
                            KeyName(_negativeKey) + " 仍按住，跳过");
                        return;
                    }

                    activity = StartOrScheduleTapLocked(_negativeKey, "松开 " + KeyName(_positiveKey));
                }
            }

            if (activity != null)
            {
                RaiseTapAction(activity);
            }
        }

        private string StartOrScheduleTapLocked(SocdKey key, string trigger)
        {
            CancelPendingActivationLocked();
            if (_activationDelayMs <= 0)
            {
                return TryStartTapLocked(key, trigger);
            }

            _activationPending = true;
            _activationKey = key;
            _activationTrigger = trigger;
            int generation = ++_activationGeneration;
            if (_activationTimer != null)
            {
                _activationTimer.Dispose();
            }

            _activationTimer = new Timer(OnActivationTimer, generation,
                _activationDelayMs, Timeout.Infinite);
            DebugTrace.Write("引擎：安排 " + _activationDelayMs + " ms 后反向补键 " +
                KeyName(key) + "（" + trigger + "）");
            return trigger + " → 等待 " + _activationDelayMs + " ms 后反向补键 " + KeyName(key);
        }

        private void CancelPendingActivationLocked()
        {
            if (!_activationPending)
            {
                return;
            }

            _activationPending = false;
            _activationKey = default(SocdKey);
            _activationTrigger = null;
            _activationGeneration++;
            if (_activationTimer != null)
            {
                _activationTimer.Dispose();
                _activationTimer = null;
            }
        }

        private void OnActivationTimer(object state)
        {
            int generation = (int)state;
            string activity = null;
            lock (_gate)
            {
                if (_disposed || !_activationPending || generation != _activationGeneration)
                {
                    return;
                }

                _activationPending = false;
                SocdKey key = _activationKey;
                string trigger = _activationTrigger;
                _activationKey = default(SocdKey);
                _activationTrigger = null;

                if (!_enabled)
                {
                    return;
                }

                activity = TryStartTapLocked(key, trigger + "（延迟 " + _activationDelayMs + " ms）");
            }

            if (activity != null)
            {
                RaiseTapAction(activity);
            }
        }

        private string TryStartTapLocked(SocdKey key, string trigger)
        {
            if (!_enabled || _tapActive || _syntheticDown)
            {
                return null;
            }

            if (AllowTrigger != null)
            {
                try
                {
                    if (!AllowTrigger())
                    {
                        return null;
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("前台进程过滤异常", exception);
                    return null;
                }
            }

            long now = Stopwatch.GetTimestamp();
            if (_lastTapTimestamp != 0)
            {
                double elapsedMs = (now - _lastTapTimestamp) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs < _cooldownMs)
                {
                    return null;
                }
            }

            bool keyDownSucceeded;
            try
            {
                keyDownSucceeded = _emitter.KeyDown(key);
            }
            catch (Exception exception)
            {
                Log.Error("发送反向补键按下失败", exception);
                return null;
            }

            if (!keyDownSucceeded)
            {
                Log.Warn("反向补键 " + KeyName(key) + " 按下失败" + SendErrorSuffix() +
                    "，本次不发送按键。");
                return null;
            }

            _tapActive = true;
            _tapKey = key;
            _syntheticDown = true;
            _syntheticKey = key;
            _releaseFailureCount = 0;
            _lastTapTimestamp = now;

            try
            {
                _releaseTimer.Change(Math.Max(1, _tapMs), Timeout.Infinite);
            }
            catch (Exception exception)
            {
                Log.Error("安排反向补键松开失败", exception);
                _tapActive = false;
                _tapKey = default(SocdKey);
                ReleaseSyntheticLocked(false);
                return null;
            }

            return trigger + " → 反向补键 " + KeyName(key) + "（" + _tapMs + " ms）";
        }

        private void OnReleaseTimer(object state)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (_tapActive)
                {
                    _tapActive = false;
                    _tapKey = default(SocdKey);
                    ReleaseSyntheticLocked(true);
                    return;
                }

                if (_syntheticDown)
                {
                    // A previous key-up failed (e.g. the desktop was temporarily
                    // unavailable). Retry until the synthetic key is released.
                    ReleaseSyntheticLocked(true);
                }
            }
        }

        /// <summary>
        /// Attempts to release the synthetic key. Must be called while holding
        /// <see cref="_gate"/> so the physical-keydown cancellation cannot race
        /// with the timer.
        /// </summary>
        private void ReleaseSyntheticLocked(bool allowRetry)
        {
            if (!_syntheticDown)
            {
                return;
            }

            SocdKey key = _syntheticKey;
            if (IsPhysicalHeldLocked(key))
            {
                // A real keydown for the same key has arrived. The physical key
                // now owns the key state; its release will produce the key-up.
                _syntheticDown = false;
                _syntheticKey = default(SocdKey);
                _releaseFailureCount = 0;
                return;
            }

            bool keyUpSucceeded;
            try
            {
                keyUpSucceeded = _emitter.KeyUp(key);
            }
            catch (Exception exception)
            {
                Log.Error("发送反向补键松开失败", exception);
                keyUpSucceeded = false;
            }

            if (keyUpSucceeded)
            {
                _syntheticDown = false;
                _syntheticKey = default(SocdKey);
                _releaseFailureCount = 0;
                return;
            }

            _releaseFailureCount++;
            if (_releaseFailureCount == 1)
            {
                Log.Warn("反向补键 " + KeyName(key) + " 松开失败" + SendErrorSuffix() +
                    (allowRetry ? "，将自动重试。" : "。"));
            }
            else if (_releaseFailureCount % 20 == 0)
            {
                Log.Warn("反向补键 " + KeyName(key) + " 仍未松开，继续重试（第 " +
                    _releaseFailureCount + " 次）。");
            }

            if (allowRetry && !_disposed)
            {
                try
                {
                    _releaseTimer.Change(RetryReleaseMs, Timeout.Infinite);
                }
                catch
                {
                }
            }
        }

        private void CancelTapForPhysicalKeyLocked(SocdKey key)
        {
            bool cancelTimer = false;

            if (_tapActive && _tapKey.VirtualKey == key.VirtualKey)
            {
                _tapActive = false;
                _tapKey = default(SocdKey);
                cancelTimer = true;
            }

            if (_syntheticDown && _syntheticKey.VirtualKey == key.VirtualKey)
            {
                // The physical key is now down; do not send an early key-up.
                _syntheticDown = false;
                _syntheticKey = default(SocdKey);
                _releaseFailureCount = 0;
                cancelTimer = true;
            }

            if (cancelTimer)
            {
                CancelTimerLocked();
            }
        }

        private bool IsPhysicalHeldLocked(SocdKey key)
        {
            return key.VirtualKey == _negativeKey.VirtualKey ? _negativeHeld : _positiveHeld;
        }

        private void CancelTimerLocked()
        {
            try
            {
                _releaseTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch
            {
            }
        }

        private string SendErrorSuffix()
        {
            int code = _emitter.LastErrorCode;
            if (code == 0)
            {
                return string.Empty;
            }

            string message;
            try
            {
                message = new System.ComponentModel.Win32Exception(code).Message;
            }
            catch
            {
                message = "未知错误";
            }

            return "（Win32 错误码 " + code + "：" + message + "）";
        }

        private void RaiseTapAction(string activity)
        {
            Action<string> handler = TapAction;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(activity);
            }
            catch (Exception exception)
            {
                Log.Error("写入补键记录失败", exception);
            }
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        private static string KeyName(SocdKey key)
        {
            if (key.VirtualKey == SocdKey.A.VirtualKey) return "A";
            if (key.VirtualKey == SocdKey.D.VirtualKey) return "D";
            if (key.VirtualKey == SocdKey.W.VirtualKey) return "W";
            if (key.VirtualKey == SocdKey.S.VirtualKey) return "S";
            return string.Format("VK 0x{0:X2}", key.VirtualKey);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _enabled = false;
                CancelPendingActivationLocked();

                if (_tapActive)
                {
                    _tapActive = false;
                    _tapKey = default(SocdKey);
                    CancelTimerLocked();
                }

                if (_syntheticDown)
                {
                    ReleaseSyntheticLocked(false);
                }
            }

            _releaseTimer.Dispose();
            if (_activationTimer != null)
            {
                _activationTimer.Dispose();
                _activationTimer = null;
            }
        }
    }
}
