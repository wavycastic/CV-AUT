using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Runtime.InteropServices;

namespace CvAut.Views
{
    public partial class HeaderView : UserControl
    {
        public HeaderView()
        {
            InitializeComponent();
        }

        private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window window)
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                var handle = window.TryGetPlatformHandle();
                if (handle?.HandleDescriptor == "HWND" && handle.Handle != IntPtr.Zero)
                {
                    ShowWindow(handle.Handle, SwMinimize);
                    return;
                }
            }

            window.WindowState = WindowState.Minimized;
        }

        private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                window.Close();
            }
        }

        private const int SwMinimize = 6;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
