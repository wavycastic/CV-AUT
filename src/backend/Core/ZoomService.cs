using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CvAut;

internal sealed class ZoomService : IZoomService
{
    private readonly ADBHelper _adb;

    public ZoomService(ADBHelper adb)
    {
        _adb = adb;
    }

    public void ZoomOut()
    {
        Console.WriteLine("[FSM-CS] phase=camera_zoom status=start");

        IntPtr memuParent = FindMainWindowByProcessName("MEmu");
        if (memuParent == IntPtr.Zero)
            memuParent = FindWindow(null, "MEmu");

        IntPtr bsParent = FindMainWindowByProcessName("HD-Player", "BlueStacks");
        if (bsParent == IntPtr.Zero)
            bsParent = FindWindow(null, "BlueStacks App Player");

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
            if (ok)
                Console.WriteLine("[FSM-CS] phase=camera_zoom status=success details=\"bluestacks_adb_pinch\"");
            else
                Console.WriteLine("[FSM-CS WARNING] phase=camera_zoom status=fail reason=no_confirmation");
        }
        else
        {
            Console.WriteLine("[FSM-CS WARNING] phase=camera_zoom status=skip reason=emulator_window_not_found");
        }
    }

    private static IntPtr FindMainWindowByProcessName(params string[] processNames)
    {
        foreach (string processName in processNames)
        {
            try
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.MainWindowHandle;
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
                if (attached)
                    AttachThreadInput(currentThreadId, targetThreadId, false);
            }
            Thread.Sleep(gapMs);
        }
    }

    // --- Win32 P/Invoke ---
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
}
