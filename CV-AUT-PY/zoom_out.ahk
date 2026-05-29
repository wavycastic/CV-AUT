#Requires AutoHotkey v2.0
#SingleInstance Force
SetTitleMatchMode(2)

; — find MEmu by its executable name —
hMEmu := WinExist("ahk_exe MEmu.exe")
if !hMEmu
{
    MsgBox("Error: MEmu window not found. Make sure MEmu.exe is running.")
    ExitApp()
}

; — send F3 four times into MEmu without ever stealing focus —
Loop 4
{
    SendF3ToMEmu(hMEmu)
    Sleep 1000
}
ExitApp()


; — helper: attach to MEmu’s thread and post an F3 keypress —
SendF3ToMEmu(hWnd)
{
    static WM_KEYDOWN := 0x0100
    static WM_KEYUP   := 0x0101
    static VK_F3      := 0x72

    myTid     := DllCall("GetCurrentThreadId")
    targetTid := DllCall("GetWindowThreadProcessId", "UInt", hWnd, "UIntP", 0)

    ; link our input thread with MEmu’s
    DllCall("AttachThreadInput", "UInt", myTid, "UInt", targetTid, "Int", true)

    ; post key-down & key-up into MEmu’s message queue
    DllCall("PostMessage", "UInt", hWnd, "UInt", WM_KEYDOWN, "UInt", VK_F3, "UInt", 0)
    Sleep 20
    DllCall("PostMessage", "UInt", hWnd, "UInt", WM_KEYUP,   "UInt", VK_F3, "UInt", 0)

    ; detach threads again
    DllCall("AttachThreadInput", "UInt", myTid, "UInt", targetTid, "Int", false)
}
