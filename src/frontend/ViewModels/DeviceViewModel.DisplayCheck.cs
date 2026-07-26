using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Configuration;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Configuration;
using CvAut.Services.Sessions;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Emulator display precheck. Template matching assumes 1600x900, so a mismatch is worth
    /// warning about, but it is only a warning: the probe itself can fail and the user may have
    /// set the emulator up in a way this check cannot see.
    /// </summary>
    public partial class DeviceViewModel
    {
        /// <summary>Non-blocking display precheck note (e.g. resolution/DPI mismatch) shown before/after Start.</summary>
        [ObservableProperty] private string _displayWarning = string.Empty;

        /// <summary>
        /// Best-effort display precheck: if a discovery service is available, verify the emulator
        /// is at 1600x900 / 240dpi and surface a non-blocking warning (log + <see cref="DisplayWarning"/>)
        /// when it is not. Never blocks Start — the user may have marked the display OK manually.
        /// </summary>
        private async Task WarnIfDisplayMismatchAsync()
        {
            if (_discovery is null)
            {
                return;
            }

            try
            {
                var info = await _discovery.GetDisplayInfoAsync(Device);
                if (info.Width <= 0 || info.Height <= 0 || (info.ResolutionOk && info.DpiOk))
                {
                    DisplayWarning = string.Empty;
                    return;
                }

                int expectedDpi = !string.IsNullOrWhiteSpace(Device.EmulatorType) && Device.EmulatorType.Equals("BlueStacks", StringComparison.OrdinalIgnoreCase) ? 300 : 240;
                DisplayWarning = $"Màn hình {info.Width}x{info.Height} / {info.DensityDpi}dpi khác chuẩn 1600x900 / {expectedDpi}dpi — bot có thể chạy sai.";
                AddLog(DisplayWarning, LogLevel.Warning);
            }
            catch
            {
                // Precheck is best-effort; never block Start on a probe failure.
            }
        }
    }
}
