using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Vehicle and siren lifecycle hooks.
    /// Patch points: VehicleSpawner.Spawn, FFD_SirenControl.SetSiren,
    ///               FFD_Airhorn.OnPlay, NYPDSirenController.Update (state compare).
    /// </summary>
    public static class VehicleAPI
    {
        // ── Events ──────────────────────────────────────────────────────────
        public static event Action<GameObject>? OnVehicleSpawned;

        /// <summary>Fired when any FFD siren becomes active. args: controller, new state.</summary>
        public static event Action<FFD_SirenControl, FFD_SirenControl.SirenState>? OnSirenActivated;

        /// <summary>Fired when an FFD siren turns off.</summary>
        public static event Action<FFD_SirenControl>? OnSirenDeactivated;

        public static event Action<FFD_Airhorn>? OnAirhornUsed;

        /// <summary>Fired when NYPD siren state changes (wailing/yelping/prtying/off).</summary>
        public static event Action<NYPDSirenController>? OnPoliceSirenChanged;

        // ── Bridge delegates ─────────────────────────────────────────────────
        public static Func<IReadOnlyList<FFD_SirenControl>>? GetAllSirenVehiclesImpl;

        // ── Public helpers ───────────────────────────────────────────────────
        public static IReadOnlyList<FFD_SirenControl> GetAllSirenVehicles() =>
            GetAllSirenVehiclesImpl?.Invoke() ?? Array.Empty<FFD_SirenControl>();

        // ── Internal raise ───────────────────────────────────────────────────
        internal static void RaiseVehicleSpawned(GameObject go) =>
            EmergeNYCSDK.SafeRaise(OnVehicleSpawned, go, nameof(OnVehicleSpawned));

        internal static void RaiseSirenActivated(FFD_SirenControl ctrl, FFD_SirenControl.SirenState state) =>
            EmergeNYCSDK.SafeRaise(OnSirenActivated, ctrl, state, nameof(OnSirenActivated));

        internal static void RaiseSirenDeactivated(FFD_SirenControl ctrl) =>
            EmergeNYCSDK.SafeRaise(OnSirenDeactivated, ctrl, nameof(OnSirenDeactivated));

        internal static void RaiseAirhornUsed(FFD_Airhorn horn) =>
            EmergeNYCSDK.SafeRaise(OnAirhornUsed, horn, nameof(OnAirhornUsed));

        internal static void RaisePoliceSirenChanged(NYPDSirenController ctrl) =>
            EmergeNYCSDK.SafeRaise(OnPoliceSirenChanged, ctrl, nameof(OnPoliceSirenChanged));
    }
}
