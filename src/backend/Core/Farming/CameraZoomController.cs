using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: thu nhỏ camera trong game theo từng loại giả lập
    /// (MEmu qua phím F3 gửi vào cửa sổ, BlueStacks qua pinch ADB).
    /// </summary>
    internal sealed class CameraZoomController
    {
        private readonly ADBHelper _adb;

        public CameraZoomController(ADBHelper adb)
        {
            _adb = adb;
        }

        public void ZoomOut()
        {
            Console.WriteLine("[FSM-CS] phase=camera_zoom status=start");
            IntPtr memuParent = FindMainWindowByProcessName("MEmu");
            if (memuParent == IntPtr.Zero) memuParent = FindWindow(null, "MEmu");

            IntPtr bsParent = FindMainWindowByProcessName("HD-Player", "BlueStacks");
            if (bsParent == IntPtr.Zero) bsParent = FindWindow(null, "BlueStacks App Player");

            if (memuParent != IntPtr.Zero)
            {
                Console.WriteLine("[FSM-CS] phase=camera_zoom status=pending details=\"memu_detected\"");
                SendKeyToWindow(memuParent, (IntPtr)0x72, repetitions: 4, gapMs: 1000);
                Console.WriteLine("[FSM-CS] phase=camera_zoom status=success details=\"memu\"");
            }
            else if (bsParent != IntPtr.Zero)
            {
                Console.WriteLine("[FSM-CS] phase=camera_zoom status=pending details=\"bluestacks_detected\"");
                bool ok = _adb.PinchInZoomOut(count: 3, durationMs: 450, intervalMs: 500);
                Console.WriteLine(ok
                    ? "[FSM-CS] phase=camera_zoom status=success details=\"bluestacks_adb_pinch\""
                    : "[FSM-CS WARNING] phase=camera_zoom status=fail reason=no_confirmation");
            }
            else
            {
                Console.WriteLine("[FSM-CS WARNING] phase=camera_zoom status=skip reason=emulator_window_not_found");
            }
        }

        // --- Win32 P/Invoke helpers ---
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static IntPtr FindMainWindowByProcessName(params string[] processNames)
        {
            foreach (string processName in processNames)
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                        if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        private static void SendKeyToWindow(IntPtr hWnd, IntPtr virtualKey, int repetitions, int gapMs)
        {
            const uint WM_KEYDOWN = 0x0100;
            const uint WM_KEYUP = 0x0101;
            uint currentThreadId = GetCurrentThreadId();
            uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

            for (int i = 0; i < repetitions; i++)
            {
                bool attached = false;
                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                    attached = AttachThreadInput(currentThreadId, targetThreadId, true);
                try
                {
                    PostMessage(hWnd, WM_KEYDOWN, virtualKey, IntPtr.Zero);
                    Thread.Sleep(20);
                    PostMessage(hWnd, WM_KEYUP, virtualKey, IntPtr.Zero);
                }
                finally
                {
                    if (attached) AttachThreadInput(currentThreadId, targetThreadId, false);
                }
                if (i < repetitions - 1) Thread.Sleep(gapMs);
            }
        }
    }
}
