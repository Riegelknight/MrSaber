using System;
using UnityEngine;

namespace BeatSaber
{
    /// <summary>
    /// Liefert den Zustand der PICO-Controller einmal pro Frame:
    /// X/Y (links), A/B (rechts), Grip (seitliche Taste) und Haupt-/Index-Trigger mit
    /// Flanken-Erkennung und Haltezeit. Nutzt UnityEngine.XR.InputDevices — unabhängig
    /// von XRI/InputSystem.
    /// Taste-Zuordnung: Grip (seitlich) = Easter-Egg-Doppelklick (Schwert ein-/ausfahren),
    /// Haupt-/Index-Trigger = "Bestätigen" in der Menünavigation.
    /// </summary>
    public sealed class ControllerInput
    {
        public bool LeftXPressed { get; private set; }   // X = primaryButton links
        public bool LeftYPressed { get; private set; }   // Y = secondaryButton links
        public bool RightAPressed { get; private set; }  // A = primaryButton rechts
        public bool RightBPressed { get; private set; }  // B = secondaryButton rechts

        public bool LeftXWasPressedThisFrame { get; private set; }
        public bool LeftYWasPressedThisFrame { get; private set; }
        public bool RightAWasPressedThisFrame { get; private set; }
        public bool RightBWasPressedThisFrame { get; private set; }

        /// <summary>Seitliche Taste (Grip) — Flanke gedrückt (NUR Easter-Egg-Doppelklick: Schwert ein-/ausfahren).</summary>
        public bool LeftGripWasPressedThisFrame { get; private set; }
        public bool RightGripWasPressedThisFrame { get; private set; }

        /// <summary>Haupt-/Index-Trigger — Flanke gedrückt ("Bestätigen" in der Menünavigation).</summary>
        public bool LeftMainTriggerWasPressedThisFrame { get; private set; }
        public bool RightMainTriggerWasPressedThisFrame { get; private set; }

        /// <summary>Doppelklick (2 Pressen binnen ~0,4 s) der Grip-Taste — Easter Egg:
        /// fährt das jeweilige Lichtschwert ein/aus (jederzeit möglich).</summary>
        public bool LeftGripDoubleClicked { get; private set; }
        public bool RightGripDoubleClicked { get; private set; }

        public bool LeftXReleasedThisFrame { get; private set; }
        public bool LeftYReleasedThisFrame { get; private set; }
        public bool RightAReleasedThisFrame { get; private set; }
        public bool RightBReleasedThisFrame { get; private set; }

        /// <summary>Losgelassen nach &lt; 2 s (kurz) — zum Öffnen/Schließen des Menüs.</summary>
        public bool LeftYReleasedShort { get; private set; }
        public bool RightBReleasedShort { get; private set; }

        /// <summary>Analog-Stick („Joystick") je Hand: x/y −1..1 (nach oben/vorn = +1).</summary>
        public float LeftThumbX { get; private set; }
        public float LeftThumbY { get; private set; }
        public float RightThumbX { get; private set; }
        public float RightThumbY { get; private set; }

        /// <summary>Y+B gemeinsam für ≥ 2 s gehalten — in genau dem Frame true (Quit).</summary>
        public bool BothYBHeldLong { get; private set; }
        private bool _bothLongFired;

        /// <summary>Y allein ≥ 2 s gehalten (einmalig pro Haltedauer; Quit).</summary>
        public bool LeftYHeldLong { get; private set; }
        private bool _leftYLongFired;

        /// <summary>B allein ≥ 2 s gehalten (einmalig pro Haltedauer; Quit).</summary>
        public bool RightBHeldLong { get; private set; }
        private bool _rightBLongFired;

        /// <summary>Dedizierte Menü-Taste ≥ 2 s gehalten (einmalig pro Haltedauer; Quit).</summary>
        public bool LeftMenuHeldLong { get; private set; }
        private bool _menuLongFired;

        /// <summary>Haltezeit der dedizierten Menü-Taste (0 wenn nicht gedrückt).</summary>
        public float LeftMenuHoldTime { get; private set; }

        /// <summary>Haltezeit (s) der jeweiligen Taste, 0 wenn nicht gedrückt.</summary>
        public float LeftYHoldTime { get; private set; }
        public float RightBHoldTime { get; private set; }
        public float BothYBHoldTime { get; private set; } // ab Start des gemeinsamen Drucks

        private bool _leftXWas;
        private bool _leftYWas;
        private bool _rightAWas;
        private bool _rightBWas;
        private bool _leftGripWas;
        private bool _rightGripWas;
        private bool _leftMainTriggerWas;
        private bool _rightMainTriggerWas;
        private float _lastLeftGripPress = -1f;
        private float _lastRightGripPress = -1f;
        private const float GripDoubleClickWindow = 0.4f;
        private float _leftYDownSince;
        private float _rightBDownSince;
        private float _bothDownSince = -1f;
        private float _menuDownSince = -1f;

        private UnityEngine.XR.InputDevice _left;
        private UnityEngine.XR.InputDevice _right;

        public ControllerInput()
        {
            RefreshDevices();
        }

        private void RefreshDevices()
        {
            if (!_left.isValid) _left = GetDevice(UnityEngine.XR.XRNode.LeftHand);
            if (!_right.isValid) _right = GetDevice(UnityEngine.XR.XRNode.RightHand);
        }

        // Wiederverwendeter Puffer: keine List-Allokation, wenn Controller (neu) erkannt
        // werden müssen (z. B. nach kurzem Tracking-Verlust).
        private static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> _deviceBuffer
            = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>(2);

        private static UnityEngine.XR.InputDevice GetDevice(UnityEngine.XR.XRNode node)
        {
            _deviceBuffer.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(node, _deviceBuffer);
            for (int i = 0; i < _deviceBuffer.Count; i++)
            {
                if (_deviceBuffer[i].isValid) return _deviceBuffer[i];
            }
            return default;
        }

        /// <summary>Soll jeden Frame aufgerufen werden.</summary>
        public void Update()
        {
            RefreshDevices();

            bool x = ReadButton(_left, UnityEngine.XR.CommonUsages.primaryButton);
            bool y = ReadButton(_left, UnityEngine.XR.CommonUsages.secondaryButton);
            bool a = ReadButton(_right, UnityEngine.XR.CommonUsages.primaryButton);
            bool b = ReadButton(_right, UnityEngine.XR.CommonUsages.secondaryButton);
            // Seitliche Taste (Grip) — NUR Easter-Egg-Doppelklick (Schwert ein-/ausfahren)
            bool lg = ReadButton(_left, UnityEngine.XR.CommonUsages.gripButton);
            bool rg = ReadButton(_right, UnityEngine.XR.CommonUsages.gripButton);
            // Haupt-/Index-Trigger — "Bestätigen" in der Menünavigation
            bool lmt = ReadButton(_left, UnityEngine.XR.CommonUsages.triggerButton);
            bool rmt = ReadButton(_right, UnityEngine.XR.CommonUsages.triggerButton);
            // Dedizierte Menü-Taste (systemseitig auf dem Controller, "Menütaste")
            bool menu = ReadButton(_left, UnityEngine.XR.CommonUsages.menuButton)
                        || ReadButton(_right, UnityEngine.XR.CommonUsages.menuButton);

            // Analog-Sticks (Joystick)
            Vector2 lstick = ReadAxis(_left, UnityEngine.XR.CommonUsages.primary2DAxis);
            Vector2 rstick = ReadAxis(_right, UnityEngine.XR.CommonUsages.primary2DAxis);
            LeftThumbX = lstick.x; LeftThumbY = lstick.y;
            RightThumbX = rstick.x; RightThumbY = rstick.y;

            // ----- Flanken -----
            LeftXWasPressedThisFrame = x && !_leftXWas;
            LeftYWasPressedThisFrame = y && !_leftYWas;
            RightAWasPressedThisFrame = a && !_rightAWas;
            RightBWasPressedThisFrame = b && !_rightBWas;
            LeftGripWasPressedThisFrame = lg && !_leftGripWas;
            RightGripWasPressedThisFrame = rg && !_rightGripWas;
            LeftMainTriggerWasPressedThisFrame = lmt && !_leftMainTriggerWas;
            RightMainTriggerWasPressedThisFrame = rmt && !_rightMainTriggerWas;

            // ---- Doppelklick (Easter Egg): zwei Grip-Drucke (SEITLICHE Taste) binnen
            // eines kurzen Fensters fahren das jeweilige Lichtschwert ein/aus. ----
            LeftGripDoubleClicked = false;
            RightGripDoubleClicked = false;
            if (LeftGripWasPressedThisFrame)
            {
                float now = Time.unscaledTime;
                LeftGripDoubleClicked = _lastLeftGripPress >= 0f
                                        && now - _lastLeftGripPress <= GripDoubleClickWindow;
                // Nach einem Doppelklick neu zählen; abgelaufene Einzeldrücke werden
                // durch die Fenster-Prüfung oben automatisch verworfen.
                _lastLeftGripPress = LeftGripDoubleClicked ? -1f : now;
            }
            if (RightGripWasPressedThisFrame)
            {
                float now = Time.unscaledTime;
                RightGripDoubleClicked = _lastRightGripPress >= 0f
                                         && now - _lastRightGripPress <= GripDoubleClickWindow;
                _lastRightGripPress = RightGripDoubleClicked ? -1f : now;
            }

            LeftXReleasedThisFrame = !x && _leftXWas;
            LeftYReleasedThisFrame = !y && _leftYWas;
            RightAReleasedThisFrame = !a && _rightAWas;
            RightBReleasedThisFrame = !b && _rightBWas;

            // ----- Haltezeiten -----
            if (y && !_leftYWas) _leftYDownSince = Time.unscaledTime;
            if (b && !_rightBWas) _rightBDownSince = Time.unscaledTime;

            LeftYHoldTime = y ? Time.unscaledTime - _leftYDownSince : 0f;
            RightBHoldTime = b ? Time.unscaledTime - _rightBDownSince : 0f;

            // Kurzes Loslassen (Dauer < 2 s)
            LeftYReleasedShort = (!y && _leftYWas) && (Time.unscaledTime - _leftYDownSince) < 2f;
            RightBReleasedShort = (!b && _rightBWas) && (Time.unscaledTime - _rightBDownSince) < 2f;

            // Y+B gemeinsam
            bool both = y && b;
            if (both && _bothDownSince < 0f) _bothDownSince = Time.unscaledTime;
            if (!both) _bothDownSince = -1f;
            BothYBHoldTime = both ? Time.unscaledTime - _bothDownSince : 0f;

            if (both && BothYBHoldTime >= 2f && !_bothLongFired)
            {
                _bothLongFired = true;
                BothYBHeldLong = true;
            }
            else
            {
                BothYBHeldLong = false;
            }
            if (!both) _bothLongFired = false;

            // ---- Einzeltasten lang halten (Rund-um-Quit) ----
            if (y && LeftYHoldTime >= 2f && !_leftYLongFired)
            {
                _leftYLongFired = true;
                LeftYHeldLong = true;
            }
            else
            {
                LeftYHeldLong = false;
            }
            if (!y) _leftYLongFired = false;

            if (b && RightBHoldTime >= 2f && !_rightBLongFired)
            {
                _rightBLongFired = true;
                RightBHeldLong = true;
            }
            else
            {
                RightBHeldLong = false;
            }
            if (!b) _rightBLongFired = false;

            // Dedizierte Menü-Taste
            if (menu && _menuDownSince < 0f) _menuDownSince = Time.unscaledTime;
            if (!menu) _menuDownSince = -1f;
            LeftMenuHoldTime = menu ? Time.unscaledTime - Mathf.Max(0f, _menuDownSince) : 0f;
            if (menu && LeftMenuHoldTime >= 2f && !_menuLongFired)
            {
                _menuLongFired = true;
                LeftMenuHeldLong = true;
            }
            else
            {
                LeftMenuHeldLong = false;
            }
            if (!menu) _menuLongFired = false;

            LeftXPressed = x;
            LeftYPressed = y;
            RightAPressed = a;
            RightBPressed = b;

            _leftXWas = x;
            _leftYWas = y;
            _rightAWas = a;
            _rightBWas = b;
            _leftGripWas = lg;
            _rightGripWas = rg;
            _leftMainTriggerWas = lmt;
            _rightMainTriggerWas = rmt;
        }

        private static bool ReadButton(UnityEngine.XR.InputDevice device, UnityEngine.XR.InputFeatureUsage<bool> usage)
        {
            if (!device.isValid) return false;
            return device.TryGetFeatureValue(usage, out bool value) && value;
        }

        private static Vector2 ReadAxis(UnityEngine.XR.InputDevice device, UnityEngine.XR.InputFeatureUsage<Vector2> usage)
        {
            if (!device.isValid) return Vector2.zero;
            return device.TryGetFeatureValue(usage, out Vector2 value) ? value : Vector2.zero;
        }
    }
}
