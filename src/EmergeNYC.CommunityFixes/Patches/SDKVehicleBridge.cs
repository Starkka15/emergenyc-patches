using HarmonyLib;
using UnityEngine;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.CommunityFixes.Patches
{
    // T14: Fire VehicleAPI events from FFD_SirenControl, FFD_Airhorn, NYPDSirenController.
    // Note: Vehicle is a [Serializable] POCO (not MonoBehaviour), so OnEnable can't be patched.
    // FFD_SirenControl.OnEnable is used as the vehicle-spawn signal — all fire dept vehicles have it.

    /// <summary>
    /// FFD_SirenControl.OnEnable fires when a fire department vehicle activates/spawns.
    /// </summary>
    [HarmonyPatch(typeof(FFD_SirenControl), "OnEnable")]
    public static class SDK_FFD_SirenControl_OnEnable
    {
        [HarmonyPostfix]
        public static void Postfix(FFD_SirenControl __instance) =>
            VehicleAPI.RaiseVehicleSpawned(__instance.gameObject);
    }

    [HarmonyPatch(typeof(FFD_SirenControl), "SetSiren")]
    public static class SDK_FFD_SetSiren
    {
        [HarmonyPostfix]
        public static void Postfix(FFD_SirenControl __instance,
            FFD_SirenControl.SirenState state, bool rumbleOn)
        {
            if (state == FFD_SirenControl.SirenState.Off)
                VehicleAPI.RaiseSirenDeactivated(__instance);
            else
                VehicleAPI.RaiseSirenActivated(__instance, state);
        }
    }

    /// <summary>
    /// Extend the existing YieldAirhornPatch — also fire SDK event.
    /// (YieldAirhornPatch already patches FFD_Airhorn.OnPlay; this is a second postfix on the same method.)
    /// </summary>
    [HarmonyPatch(typeof(FFD_Airhorn), "OnPlay")]
    public static class SDK_FFD_Airhorn_OnPlay
    {
        [HarmonyPostfix]
        public static void Postfix(FFD_Airhorn __instance) =>
            VehicleAPI.RaiseAirhornUsed(__instance);
    }

    /// <summary>
    /// Detect NYPD siren state changes by comparing previous state each Update.
    /// V14: read-only postfix, no game method calls.
    /// </summary>
    [HarmonyPatch(typeof(NYPDSirenController), "Update")]
    public static class SDK_NYPDSiren_Update
    {
        private static readonly System.Collections.Generic.Dictionary<int, (bool w, bool y, bool p)>
            _prev = new();

        [HarmonyPostfix]
        public static void Postfix(NYPDSirenController __instance)
        {
            int id = __instance.GetInstanceID();
            bool w = __instance.wailing, y = __instance.yelping, p = __instance.prtying;

            if (!_prev.TryGetValue(id, out var prev))
            {
                _prev[id] = (w, y, p);
                return;
            }

            if (w != prev.w || y != prev.y || p != prev.p)
            {
                _prev[id] = (w, y, p);
                VehicleAPI.RaisePoliceSirenChanged(__instance);
            }
        }
    }
}
