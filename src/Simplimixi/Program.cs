using Avalonia;
using System;

namespace CvAut
{
    /// <summary>
    /// Avalonia UI entry point. Replaces the former headless Core/Program.cs.
    /// </summary>
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't
        // initialized yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Enforce release integrity / anti-debug policy before the UI starts.
            // No-op in DEBUG (ReleaseSecurity short-circuits). In RELEASE it is fail-closed:
            // a ReleaseSecurityException is thrown on tamper/debugger/manifest mismatch,
            // which terminates the process before any window opens. AOT-safe (no reflection).
            ReleaseSecurity.EnforceStartupPolicy();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by the visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
