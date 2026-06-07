using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CvAut.WpfApp.Services
{
    public sealed class UpdateManifest
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("force_update")]
        public bool ForceUpdate { get; init; }

        [JsonPropertyName("min_supported_version")]
        public string? MinSupportedVersion { get; init; }

        [JsonPropertyName("notes")]
        public string Notes { get; init; } = string.Empty;
    }

    public sealed class UpdateDecision
    {
        public UpdateDecision(UpdateManifest manifest, Version currentVersion, Version latestVersion, bool isForced)
        {
            Manifest = manifest;
            CurrentVersion = currentVersion;
            LatestVersion = latestVersion;
            IsForced = isForced;
        }

        public UpdateManifest Manifest { get; }
        public Version CurrentVersion { get; }
        public Version LatestVersion { get; }
        public bool IsForced { get; }
    }

    public sealed class UpdateService
    {
        private const string ManifestUrl = "https://simplimixi.pages.dev/update.json";
        private static readonly string UpdateLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpliMixi",
            "logs",
            "update-check.log");
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public async Task<UpdateDecision?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            WriteLog($"check_start manifest={ManifestUrl}");

            using var response = await HttpClient.GetAsync(ManifestUrl, cancellationToken).ConfigureAwait(false);
            WriteLog($"http_status code={(int)response.StatusCode}");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Url))
            {
                WriteLog("check_skip reason=invalid_manifest");
                return null;
            }

            Version currentVersion = GetCurrentVersion();
            WriteLog($"manifest version={manifest.Version} url={manifest.Url} force={manifest.ForceUpdate} min={manifest.MinSupportedVersion ?? "<none>"} current={currentVersion}");

            if (!TryParseVersion(manifest.Version, out Version? parsedLatestVersion) || parsedLatestVersion == null)
            {
                WriteLog("check_skip reason=invalid_version");
                return null;
            }

            if (parsedLatestVersion <= currentVersion)
            {
                WriteLog($"check_skip reason=not_newer latest={parsedLatestVersion} current={currentVersion}");
                return null;
            }

            bool isForced = manifest.ForceUpdate || IsBelowMinimumSupportedVersion(currentVersion, manifest.MinSupportedVersion);
            WriteLog($"check_update_available latest={parsedLatestVersion} current={currentVersion} forced={isForced}");
            return new UpdateDecision(manifest, currentVersion, parsedLatestVersion, isForced);
        }

        public static async Task<string> DownloadInstallerAsync(UpdateDecision decision, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            string updatesDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SimpliMixi",
                "updates");
            Directory.CreateDirectory(updatesDirectory);

            string fileName = GetInstallerFileName(decision.Manifest.Url, decision.LatestVersion);
            string installerPath = Path.Combine(updatesDirectory, fileName);
            string temporaryPath = installerPath + ".download";

            WriteLog($"download_start url={decision.Manifest.Url} path={installerPath}");
            using var response = await HttpClient.GetAsync(decision.Manifest.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            WriteLog($"download_http_status code={(int)response.StatusCode}");
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream destinationStream = File.Create(temporaryPath);

            var buffer = new byte[1024 * 128];
            long totalBytesRead = 0;
            while (true)
            {
                int bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalBytesRead += bytesRead;

                if (contentLength is > 0)
                {
                    progress?.Report(Math.Clamp((double)totalBytesRead / contentLength.Value, 0, 1));
                }
            }

            destinationStream.Close();
            File.Move(temporaryPath, installerPath, overwrite: true);
            progress?.Report(1);
            WriteLog($"download_complete path={installerPath} bytes={totalBytesRead}");
            return installerPath;
        }

        public static void StartInstallerAndExit(string installerPath)
        {
            WriteLog($"install_start path={installerPath}");
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LAUNCHAPP=1",
                UseShellExecute = true,
                Verb = "runas"
            });
        }

        private static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        }

        private static bool IsBelowMinimumSupportedVersion(Version currentVersion, string? minSupportedVersion)
        {
            return TryParseVersion(minSupportedVersion, out Version? minimumVersion) && currentVersion < minimumVersion;
        }

        public static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UpdateLogPath)!);
                File.AppendAllText(UpdateLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Update logs are diagnostic only; never block app startup because of logging.
            }
        }

        private static string GetInstallerFileName(string url, Version version)
        {
            try
            {
                string fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (!string.IsNullOrWhiteSpace(fileName) && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return fileName;
                }
            }
            catch
            {
                // Fall back to a deterministic local filename if the manifest URL is malformed.
            }

            return $"SimpliMixi-v{version}-Setup.exe";
        }

        private static bool TryParseVersion(string? value, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Version.TryParse(value.Trim(), out version);
        }
    }
}
