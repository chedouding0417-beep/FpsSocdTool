using System;

namespace FpsSocdTool
{
    /// <summary>
    /// Runs two independent SOCD axes: A/D and W/S. Each axis has its own
    /// physical state, cooldown and auto-release timer.
    /// </summary>
    internal sealed class SocdEngineGroup : IDisposable
    {
        private readonly SocdEngine _horizontal;
        private readonly SocdEngine _vertical;

        internal event Action<string> TapAction;

        internal SocdEngineGroup(IKeyEmitter emitter)
        {
            _horizontal = new SocdEngine(emitter, SocdKey.A, SocdKey.D);
            _vertical = new SocdEngine(emitter, SocdKey.W, SocdKey.S);
            _horizontal.TapAction += RaiseTapAction;
            _vertical.TapAction += RaiseTapAction;
        }

        internal bool IsEnabled
        {
            get { return _horizontal.IsEnabled; }
        }

        internal bool HasPendingSyntheticKey
        {
            get { return _horizontal.HasPendingSyntheticKey || _vertical.HasPendingSyntheticKey; }
        }

        internal int TapMs
        {
            get { return _horizontal.TapMs; }
            set
            {
                _horizontal.TapMs = value;
                _vertical.TapMs = value;
            }
        }

        internal int CooldownMs
        {
            get { return _horizontal.CooldownMs; }
            set
            {
                _horizontal.CooldownMs = value;
                _vertical.CooldownMs = value;
            }
        }

        internal bool SkipIfOppositeHeld
        {
            get { return _horizontal.SkipIfOppositeHeld; }
            set
            {
                _horizontal.SkipIfOppositeHeld = value;
                _vertical.SkipIfOppositeHeld = value;
            }
        }

        internal bool UseScanCodes
        {
            get { return _horizontal.UseScanCodes; }
            set
            {
                _horizontal.UseScanCodes = value;
                _vertical.UseScanCodes = value;
            }
        }

        internal Func<bool> AllowTrigger
        {
            set
            {
                _horizontal.AllowTrigger = value;
                _vertical.AllowTrigger = value;
            }
        }

        internal void SetEnabled(bool enabled)
        {
            _horizontal.SetEnabled(enabled);
            _vertical.SetEnabled(enabled);
        }

        internal void SetPhysicalStates(bool aDown, bool dDown, bool wDown, bool sDown)
        {
            _horizontal.SetPhysicalStates(aDown, dDown);
            _vertical.SetPhysicalStates(wDown, sDown);
        }

        internal void ProcessKey(uint virtualKey, uint scanCode, bool isDown, bool injected, IntPtr extraInfo)
        {
            if (Matches(virtualKey, scanCode, SocdKey.A, SocdKey.D))
            {
                _horizontal.ProcessKey(virtualKey, scanCode, isDown, injected, extraInfo);
                return;
            }

            if (Matches(virtualKey, scanCode, SocdKey.W, SocdKey.S))
            {
                _vertical.ProcessKey(virtualKey, scanCode, isDown, injected, extraInfo);
            }
        }

        private static bool Matches(uint virtualKey, uint scanCode, SocdKey first, SocdKey second)
        {
            return virtualKey == first.VirtualKey || virtualKey == second.VirtualKey ||
                scanCode == first.ScanCode || scanCode == second.ScanCode;
        }

        private void RaiseTapAction(string text)
        {
            Action<string> handler = TapAction;
            if (handler != null)
            {
                handler(text);
            }
        }

        public void Dispose()
        {
            _horizontal.TapAction -= RaiseTapAction;
            _vertical.TapAction -= RaiseTapAction;
            _horizontal.Dispose();
            _vertical.Dispose();
        }
    }
}
