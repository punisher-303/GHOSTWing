using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Diagnostics;

namespace GHOSTWing
{
    public static class GlobalInputHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;

        public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        private static LowLevelHookProc _keyboardProc = KeyboardHookCallback;
        private static LowLevelHookProc _mouseProc = MouseHookCallback;
        private static IntPtr _keyboardHookID = IntPtr.Zero;
        private static IntPtr _mouseHookID = IntPtr.Zero;

        public static event Action<string>? OnShortcutPressed;

        // Button state tracking
        public static bool IsLeftButtonPressed { get; private set; }
        public static bool IsRightButtonPressed { get; private set; }

        // Physical mouse delta tracking (raw movement from sensor)
        private static int _accumulatedPhysicalX = 0;
        private static int _accumulatedPhysicalY = 0;
        private static readonly object _deltaLock = new object();

        public static (int X, int Y) GetAndResetPhysicalDeltas()
        {
            lock (_deltaLock)
            {
                int x = _accumulatedPhysicalX;
                int y = _accumulatedPhysicalY;
                _accumulatedPhysicalX = 0;
                _accumulatedPhysicalY = 0;
                return (x, y);
            }
        }

        // Modifier state tracking
        private static bool isCtrlPressed = false;
        private static bool isShiftPressed = false;
        private static bool isAltPressed = false;

        public static void Start()
        {
            if (_keyboardHookID == IntPtr.Zero)
                _keyboardHookID = SetHook(WH_KEYBOARD_LL, _keyboardProc);
            if (_mouseHookID == IntPtr.Zero)
                _mouseHookID = SetHook(WH_MOUSE_LL, _mouseProc);
        }

        public static void Stop()
        {
            if (_keyboardHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookID);
                _keyboardHookID = IntPtr.Zero;
            }
            if (_mouseHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookID);
                _mouseHookID = IntPtr.Zero;
            }
        }

        private static IntPtr SetHook(int hookId, LowLevelHookProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                IntPtr hMod = GetModuleHandle(curModule.ModuleName!);
                return SetWindowsHookEx(hookId, proc, hMod, 0);
            }
        }

        private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                Key key = KeyInterop.KeyFromVirtualKey(kb.vkCode);

                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                if (key == Key.LeftCtrl || key == Key.RightCtrl) isCtrlPressed = isDown;
                if (key == Key.LeftShift || key == Key.RightShift) isShiftPressed = isDown;
                if (key == Key.LeftAlt || key == Key.RightAlt || key == Key.System) isAltPressed = isDown;

                if (isDown)
                {
                    // Ignore if it's just a modifier key
                    if (key != Key.LeftCtrl && key != Key.RightCtrl &&
                        key != Key.LeftShift && key != Key.RightShift &&
                        key != Key.LeftAlt && key != Key.RightAlt && key != Key.System)
                    {
                        string shortcut = BuildShortcutString(key.ToString());
                        OnShortcutPressed?.Invoke(shortcut);
                    }
                }
            }
            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;

                // Track button states for the recoil engine
                if (msg == WM_LBUTTONDOWN) IsLeftButtonPressed = true;
                else if (msg == WM_LBUTTONUP) IsLeftButtonPressed = false;
                else if (msg == WM_RBUTTONDOWN) IsRightButtonPressed = true;
                else if (msg == WM_RBUTTONUP) IsRightButtonPressed = false;

                // Track physical movement deltas
                if (msg == 0x0200) // WM_MOUSEMOVE
                {
                    var mouseHookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    UpdatePhysicalDelta(mouseHookStruct.pt.x, mouseHookStruct.pt.y);
                }
                
                if (msg == WM_MBUTTONDOWN || msg == WM_XBUTTONDOWN)
                {
                    string buttonName = "";
                    if (msg == WM_MBUTTONDOWN) buttonName = "MButton";
                    if (msg == WM_XBUTTONDOWN)
                    {
                        var mouseHookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        uint xButton = (mouseHookStruct.mouseData >> 16) & 0xFFFF;
                        if (xButton == 1) buttonName = "XButton1";
                        if (xButton == 2) buttonName = "XButton2";
                    }

                    if (!string.IsNullOrEmpty(buttonName))
                    {
                        string shortcut = BuildShortcutString(buttonName);
                        OnShortcutPressed?.Invoke(shortcut);
                    }
                }
            }
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        private static int _lastHookX = -1;
        private static int _lastHookY = -1;

        private static void UpdatePhysicalDelta(int x, int y)
        {
            if (_lastHookX != -1)
            {
                lock (_deltaLock)
                {
                    _accumulatedPhysicalX += (x - _lastHookX);
                    _accumulatedPhysicalY += (y - _lastHookY);
                }
            }
            _lastHookX = x;
            _lastHookY = y;
        }

        private static string BuildShortcutString(string mainKey)
        {
            string s = "";
            if (isCtrlPressed) s += "Ctrl+";
            if (isShiftPressed) s += "Shift+";
            if (isAltPressed) s += "Alt+";
            s += mainKey;
            return s;
        }
    }
}
