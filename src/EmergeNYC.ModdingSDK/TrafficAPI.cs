using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Traffic system hooks and control.
    /// Bridge delegates wired by CommunityFixes at init (V12 — avoids circular dependency).
    /// </summary>
    public static class TrafficAPI
    {
        // ── Events ──────────────────────────────────────────────────────────
        public static event Action<TSTrafficAI>? OnCarYieldStart;
        public static event Action<TSTrafficAI>? OnCarYieldEnd;
        public static event Action<TSTrafficAI>? OnCarSpawned;
        public static event Action<TSTrafficAI>? OnCarDespawned;

        // ── Bridge delegates (set by CommunityFixes.Plugin.Awake) ───────────
        public static Action<Transform>? RegisterEmergencyVehicleImpl;
        public static Action<Transform>? UnregisterEmergencyVehicleImpl;
        public static Func<IReadOnlyList<TSTrafficAI>>? GetYieldingCarsImpl;
        public static Action<float>? SetSpawnDensityImpl;

        // ── Public helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Register a custom Transform as an emergency vehicle.
        /// Traffic within siren radius will yield to it.
        /// </summary>
        public static void RegisterEmergencyVehicle(Transform t) =>
            RegisterEmergencyVehicleImpl?.Invoke(t);

        /// <summary>Unregister a previously registered emergency vehicle.</summary>
        public static void UnregisterEmergencyVehicle(Transform t) =>
            UnregisterEmergencyVehicleImpl?.Invoke(t);

        /// <summary>Returns all TS cars currently in the yield state machine.</summary>
        public static IReadOnlyList<TSTrafficAI> GetYieldingCars() =>
            GetYieldingCarsImpl?.Invoke() ?? Array.Empty<TSTrafficAI>();

        /// <summary>
        /// Override TS spawner target density (0–1 maps to 0–amount).
        /// Pass 1f to restore default.
        /// </summary>
        public static void SetSpawnDensity(float density) =>
            SetSpawnDensityImpl?.Invoke(density);

        // ── Internal raise ───────────────────────────────────────────────────
        internal static void RaiseCarYieldStart(TSTrafficAI car) =>
            EmergeNYCSDK.SafeRaise(OnCarYieldStart, car, nameof(OnCarYieldStart));

        internal static void RaiseCarYieldEnd(TSTrafficAI car) =>
            EmergeNYCSDK.SafeRaise(OnCarYieldEnd, car, nameof(OnCarYieldEnd));

        internal static void RaiseCarSpawned(TSTrafficAI car) =>
            EmergeNYCSDK.SafeRaise(OnCarSpawned, car, nameof(OnCarSpawned));

        internal static void RaiseCarDespawned(TSTrafficAI car) =>
            EmergeNYCSDK.SafeRaise(OnCarDespawned, car, nameof(OnCarDespawned));
    }
}
