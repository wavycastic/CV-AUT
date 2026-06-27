using System;
using System.Collections.Generic;
using CvAut.Models;

namespace CvAut.Services.Sessions
{
    /// <summary>
    /// Owns the live <see cref="IDeviceSession"/> instances. Phase 1 holds a single active
    /// session; Phase 3 extends to one session per device with per-device ADB connections and
    /// isolated event streams. The UI never creates sessions directly — it goes through this
    /// manager so the runtime-state boundary stays in one place.
    /// </summary>
    public interface IDeviceSessionManager : IDisposable
    {
        /// <summary>The session currently rendered in single mode (null when none).</summary>
        IDeviceSession? Active { get; }

        /// <summary>All live sessions (one in Phase 1).</summary>
        IReadOnlyList<IDeviceSession> Sessions { get; }

        /// <summary>Gets the session for <paramref name="device"/>, creating it if absent.</summary>
        IDeviceSession GetOrCreate(Device device, string configPath);

        /// <summary>Promotes the session with <paramref name="deviceId"/> to <see cref="Active"/>.</summary>
        void SetActive(string deviceId);

        /// <summary>Stops and removes the session for <paramref name="deviceId"/> (no-op if absent).</summary>
        void Remove(string deviceId);
    }
}
