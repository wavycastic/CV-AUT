using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace CvAut.Adb
{
    /// <summary>
    /// Owns UIAutomator2 package discovery, ADB forwarding, server lifecycle, and JSON-RPC gestures.
    /// </summary>
    internal sealed class UiAutomatorGestureClient : IUiAutomatorGestureClient
    {
        private const int LocalPort = 9008;
        private static readonly HttpClient SharedHttpClient = new();

        private readonly string _deviceAddress;
        private readonly IAdbCommandRunner _runner;
        private readonly HttpClient _httpClient;
        private readonly Func<string?> _findJar;
        private Process? _serverProcess;
        private bool _disposed;

        public UiAutomatorGestureClient(string deviceAddress, IAdbCommandRunner runner)
            : this(deviceAddress, runner, SharedHttpClient, FindUiAutomatorJar)
        {
        }

        internal UiAutomatorGestureClient(
            string deviceAddress,
            IAdbCommandRunner runner,
            HttpClient httpClient,
            Func<string?> findJar)
        {
            _deviceAddress = string.IsNullOrWhiteSpace(deviceAddress)
                ? throw new ArgumentException("Device address is required.", nameof(deviceAddress))
                : deviceAddress;
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _findJar = findJar ?? throw new ArgumentNullException(nameof(findJar));
        }

        public bool PinchIn(
            int count,
            int percent = 100,
            int steps = 20,
            int intervalMs = 350,
            CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (count <= 0) return false;
            if (!EnsureServer(token)) return false;

            bool anySuccess = false;
            for (int index = 0; index < count && !token.IsCancellationRequested; index++)
            {
                bool succeeded = SendJsonRpc("pinchIn", BuildPinchParameters(percent, steps), token);
                if (!succeeded)
                {
                    _runner.RunAdbCommand(_deviceAddress, "shell input tap 5 5");
                    if (token.WaitHandle.WaitOne(500)) return anySuccess;
                    succeeded = SendJsonRpc("pinchIn", BuildPinchParameters(percent, steps), token);
                }

                anySuccess |= succeeded;
                if (index < count - 1 && token.WaitHandle.WaitOne(intervalMs)) break;
            }

            return anySuccess;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_serverProcess is null) return;

            try
            {
                if (!_serverProcess.HasExited) _serverProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Log("dispose", ex.Message);
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }

        internal static JsonArray BuildPinchParameters(int percent, int steps)
            => new(
                new JsonObject
                {
                    ["mask"] = 0,
                    ["childOrSibling"] = new JsonArray(),
                    ["childOrSiblingSelector"] = new JsonArray()
                },
                percent,
                steps);

        internal static JsonObject BuildRequest(string method, JsonArray parameters)
            => new()
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = parameters
            };

        private bool EnsureServer(CancellationToken token)
        {
            _runner.RunAdbCommand(_deviceAddress, $"forward tcp:{LocalPort} tcp:{LocalPort}");
            if (Ping(token)) return true;

            string? sourceJar = _findJar();
            if (sourceJar is null)
            {
                Log("find_package", "not_found");
                return false;
            }

            string jarToPush = CacheJar(sourceJar);
            string pushResult = _runner.RunAdbCommand(
                _deviceAddress,
                $"push \"{jarToPush}\" /data/local/tmp/u2.jar");
            if (IsError(pushResult))
            {
                Log("push_package", pushResult);
                return false;
            }

            _runner.RunAdbCommand(_deviceAddress, $"forward tcp:{LocalPort} tcp:{LocalPort}");
            StartServerProcess();

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                if (Ping(token)) return true;
                if (token.WaitHandle.WaitOne(500)) break;
            }
            return false;
        }

        private void StartServerProcess()
        {
            if (_serverProcess is { HasExited: false }) return;
            _serverProcess?.Dispose();

            try
            {
                _serverProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = _runner.AdbExePath,
                    Arguments = $"-s {_deviceAddress} shell \"CLASSPATH=/data/local/tmp/u2.jar app_process / com.wetest.uia2.Main\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                });
            }
            catch (Exception ex)
            {
                Log("start_server", ex.Message);
            }
        }

        private bool Ping(CancellationToken token)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{LocalPort}/ping");
                using HttpResponseMessage response = _httpClient.Send(request, timeout.Token);
                string body = response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode && body.Trim().Equals("pong", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool SendJsonRpc(string method, JsonArray parameters, CancellationToken token)
        {
            try
            {
                string json = BuildRequest(method, parameters).ToJsonString();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"http://127.0.0.1:{LocalPort}/jsonrpc/0")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage response = _httpClient.Send(request, timeout.Token);
                string body = response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode) return false;

                using JsonDocument document = JsonDocument.Parse(body);
                return !document.RootElement.TryGetProperty("error", out _) &&
                       document.RootElement.TryGetProperty("result", out _);
            }
            catch (Exception ex)
            {
                Log("rpc", ex.Message);
                return false;
            }
        }

        private static string CacheJar(string sourceJar)
        {
            string destination = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoClashOfClan20206",
                "adb",
                "u2.jar");
            try
            {
                string? directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                if (!File.Exists(destination)) File.Copy(sourceJar, destination, overwrite: true);
                return destination;
            }
            catch (Exception ex)
            {
                Log("cache_package", ex.Message);
                return sourceJar;
            }
        }

        private static string? FindUiAutomatorJar()
        {
            string localAppDataJar = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoClashOfClan20206",
                "adb",
                "u2.jar");
            if (File.Exists(localAppDataJar)) return localAppDataJar;

            string bundledJar = Path.Combine(AppContext.BaseDirectory, "adb", "u2.jar");
            if (File.Exists(bundledJar)) return bundledJar;

            string workingDirectoryJar = Path.Combine(Directory.GetCurrentDirectory(), "adb", "u2.jar");
            return File.Exists(workingDirectoryJar) ? workingDirectoryJar : null;
        }

        private static bool IsError(string result)
            => result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);

        private static void Log(string action, string reason)
            => Console.WriteLine($"[ADB WARNING] phase=uia2 status=fail action={action} reason=\"{reason}\"");
    }
}
