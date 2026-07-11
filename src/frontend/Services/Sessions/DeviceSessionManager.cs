using System;
using System.Collections.Generic;
using CvAut.Models;

namespace CvAut.Services.Sessions
{
    /// <summary>
    /// Phase 1 <see cref="IDeviceSessionManager"/>: holds a single live session at a time.
    /// <see cref="GetOrCreate"/> returns the existing session for the same device, or creates a
    /// real <see cref="AutomationRunnerSession"/>; swap to <see cref="MockDeviceSession"/> by
    /// passing <paramref name="configPath"/> = <c>"mock"</c> to exercise the UI without a device.
    /// Phase 3 will extend this to one session per device with per-device ADB connections.
    /// </summary>
    public sealed class DeviceSessionManager : IDeviceSessionManager
    {
        private readonly Dictionary<string, IDeviceSession> _sessions = new();
        private IDeviceSession? _active;

        public IDeviceSession? Active => _active;

        /// <summary>All live sessions (one per device). Phase 3: covers every created session,
        /// so Start/Pause/Stop All and the running summary act on all devices, not just the active one.</summary>
        public IReadOnlyList<IDeviceSession> Sessions => new List<IDeviceSession>(_sessions.Values);

        public IDeviceSession GetOrCreate(Device device, string configPath)
        {
            if (_sessions.TryGetValue(device.Id, out IDeviceSession? existing))
            {
                return existing;
            }

            IDeviceSession session = string.Equals(configPath, "mock", StringComparison.OrdinalIgnoreCase)
                ? new MockDeviceSession(device.Id)
                : new AutomationRunnerSession(device.Id, configPath);

            _sessions[device.Id] = session;
            _active ??= session;
            return session;
        }

        public void SetActive(string deviceId)
        {
            if (_sessions.TryGetValue(deviceId, out IDeviceSession? session))
            {
                _active = session;
            }
        }

        public void Remove(string deviceId)
        {
            if (_sessions.TryGetValue(deviceId, out IDeviceSession? session))
            {
                try
                {
                    session.StopAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Best effort — dispose either way.
                }

                session.Dispose();
                _sessions.Remove(deviceId);
                if (_active?.DeviceId == deviceId)
                {
                    _active = null;
                }
            }
        }

        public void Dispose()
        {
            foreach (var kv in _sessions)
            {
                try
                {
                    kv.Value.Dispose();
                }
                catch
                {
                    // Best effort.
                }
            }

            _sessions.Clear();
            _active = null;
        }
    }
}
