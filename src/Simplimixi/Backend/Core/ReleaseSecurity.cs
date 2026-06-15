using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace CvAut
{
    public sealed class ReleaseSecurityException : InvalidOperationException
    {
        public ReleaseSecurityException(string message)
            : base(message)
        {
        }
    }

    public static class ReleaseSecurity
    {
        private const string IntegrityManifestRelativePath = @"security\integrity.manifest.json";
        private const string AllowDebuggerVariable = "SIMPLIMIXI_ALLOW_DEBUGGER";
        private const int RuntimeCheckIntervalMs = 15000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static int _startupValidated;
        private static long _lastRuntimeCheckTicks;

        public static void EnforceStartupPolicy()
        {
#if DEBUG
            return;
#else
            ThrowIfDebuggerDetected("startup");
            EnsureIntegrityManifestValid();
#endif
        }

        public static void EnforceRuntimePolicy()
        {
#if DEBUG
            return;
#else
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastRuntimeCheckTicks);
            if (last != 0 && now - last < RuntimeCheckIntervalMs)
            {
                return;
            }

            Interlocked.Exchange(ref _lastRuntimeCheckTicks, now);
            ThrowIfDebuggerDetected("runtime");
#endif
        }

        public static void EnsureIntegrityManifestValid()
        {
#if DEBUG
            return;
#else
            if (System.Threading.Volatile.Read(ref _startupValidated) == 1)
            {
                return;
            }

            string templatesRoot = GetTemplateRoot();
            string manifestPath = Path.Combine(AppContext.BaseDirectory, IntegrityManifestRelativePath);
            bool manifestExists = File.Exists(manifestPath);
            bool encryptedAssetsPresent = Directory.Exists(templatesRoot)
                && Directory.EnumerateFiles(templatesRoot, "*.dat", SearchOption.AllDirectories).Any();

            if (!manifestExists)
            {
                if (encryptedAssetsPresent)
                {
                    throw new ReleaseSecurityException("Release integrity manifest is missing.");
                }

                return;
            }

            IntegrityManifest? manifest = LoadManifest(manifestPath);
            if (manifest == null || manifest.Files.Count == 0)
            {
                throw new ReleaseSecurityException("Release integrity manifest is invalid.");
            }

            string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            foreach (IntegrityManifestEntry entry in manifest.Files)
            {
                ValidateManifestEntry(baseDirectory, entry);
            }

            Interlocked.Exchange(ref _startupValidated, 1);
#endif
        }

        private static void ThrowIfDebuggerDetected(string phase)
        {
            if (DebuggerBypassEnabled())
            {
                return;
            }

            if (Debugger.IsAttached || Debugger.IsLogging())
            {
                throw new ReleaseSecurityException($"Debugger detected during {phase}.");
            }

            if (NativeMethods.IsDebuggerPresent())
            {
                throw new ReleaseSecurityException($"Native debugger detected during {phase}.");
            }

            using Process process = Process.GetCurrentProcess();
            if (NativeMethods.CheckRemoteDebuggerPresent(process.Handle, out bool remoteDebuggerPresent) && remoteDebuggerPresent)
            {
                throw new ReleaseSecurityException($"Remote debugger detected during {phase}.");
            }
        }

        private static bool DebuggerBypassEnabled()
        {
            return string.Equals(Environment.GetEnvironmentVariable(AllowDebuggerVariable), "1", StringComparison.OrdinalIgnoreCase);
        }

        private static IntegrityManifest? LoadManifest(string manifestPath)
        {
            using FileStream stream = File.OpenRead(manifestPath);
            return JsonSerializer.Deserialize<IntegrityManifest>(stream, JsonOptions);
        }

        private static void ValidateManifestEntry(string baseDirectory, IntegrityManifestEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Path) || string.IsNullOrWhiteSpace(entry.Sha256))
            {
                throw new ReleaseSecurityException("Integrity manifest entry is incomplete.");
            }

            string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReleaseSecurityException("Integrity manifest entry points outside the application directory.");
            }

            if (!File.Exists(fullPath))
            {
                throw new ReleaseSecurityException($"Protected file is missing: {entry.Path}");
            }

            FileInfo fileInfo = new(fullPath);
            if (entry.Size > 0 && fileInfo.Length != entry.Size)
            {
                throw new ReleaseSecurityException($"Protected file size mismatch: {entry.Path}");
            }

            string actualHash = ComputeSha256(fullPath);
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReleaseSecurityException($"Protected file hash mismatch: {entry.Path}");
            }
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            byte[] hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static string GetTemplateRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
        }

        private sealed class IntegrityManifest
        {
            public List<IntegrityManifestEntry> Files { get; set; } = new();
        }

        private sealed class IntegrityManifestEntry
        {
            public string Path { get; set; } = string.Empty;

            public string Sha256 { get; set; } = string.Empty;

            public long Size { get; set; }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsDebuggerPresent();

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool isDebuggerPresent);
        }
    }
}

