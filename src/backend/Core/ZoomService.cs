using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CvAut;

internal sealed class ZoomService : IZoomService
{
    private readonly IADBHelper _adb;

    public ZoomService(IADBHelper adb)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
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
            Console.WriteLine(ok
                ? "[FSM-CS] phase=camera_zoom status=success details=\"bluestacks_adb_pinch\""
                : "[FSM-CS WARNING] phase=camera_zoom status=fail reason=no_confirmation");
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
                {
                    using (process)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                            return process.MainWindowHandle;
                    }
                }
            }
            catch
            {
                // Best effort: fall back to title-based lookup.
            }
        }
        return IntPtr.Zero;
    }

    private static void SendKeyToWindow(IntPtr windowHandle, IntPtr virtualKey, int repetitions, int gapMs)
    {
        const uint WmKeyDown = 0x0100;
        const uint WmKeyUp = 0x0101;

        uint currentThreadId = GetCurrentThreadId();
        uint targetThreadId = GetWindowThreadProcessId(windowHandle, out _);

        for (int index = 0; index < repetitions; index++)
        {
            bool attached = targetThreadId != 0 &&
                            targetThreadId != currentThreadId &&
                            AttachThreadInput(currentThreadId, targetThreadId, true);
            try
            {
                PostMessage(windowHandle, WmKeyDown, virtualKey, IntPtr.Zero);
                Thread.Sleep(20);
                PostMessage(windowHandle, WmKeyUp, virtualKey, IntPtr.Zero);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThreadId, targetThreadId, false);
            }
            Thread.Sleep(gapMs);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
