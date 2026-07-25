using System;
using Avalonia.Media.Imaging;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Resolves the vendor icon shipped under assets/AppIcon for an emulator type.
    /// Falls back to the generic Android icon, then to null, because a missing icon file
    /// must never take the device list down.
    /// </summary>
    internal static class EmulatorIconLoader
    {
        internal static Bitmap? Load(string? emulatorType)
        {
            try
            {
                string type = emulatorType ?? string.Empty;
                string resourcePath = "android.ico";
                if (type.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase))
                    resourcePath = "bluestacks.ico";
                else if (type.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase))
                    resourcePath = "ldplayer.png";
                else if (type.Contains("MEmu", StringComparison.OrdinalIgnoreCase))
                    resourcePath = "memu.ico";

                string appDir = AppContext.BaseDirectory;
                string filePath = System.IO.Path.Combine(appDir, "assets", "AppIcon", resourcePath);
                if (!System.IO.File.Exists(filePath))
                {
                    filePath = System.IO.Path.Combine(appDir, "assets", "AppIcon", "android.ico");
                }
                if (System.IO.File.Exists(filePath))
                {
                    return new Bitmap(filePath);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
