using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for CharacterAirHandler:
    /// 1. GUARANTEED CRASH: OnExitTrigger sets airZone = null, then immediately tries to
    ///    access airZone.current_o2Level on the next line. NullReferenceException every time.
    ///    The null check on line 78 is dead code because it comes after the null assignment.
    ///    Fix: Save airZone reference before nulling it, use saved reference for the check.
    /// 2. Method names are swapped: OnEnableAirMask() disables the mask (sets airmaskState=false)
    ///    and OnDisableAirMask() enables it (sets airmaskState=true). These are likely called
    ///    from UI buttons, so swapping them would break existing references. Instead, we note
    ///    this as a known oddity but don't change it to preserve compatibility.
    /// 3. OnStayTrigger: accesses airZone.current_o2Level without null check after the
    ///    "if (airZone != null)" block exits (line 70 is outside the if block)
    /// 4. Awake: photonView.Owner can be null in offline/singleplayer mode, causing NRE
    /// </summary>
    [HarmonyPatch(typeof(CharacterAirHandler))]
    public static class CharacterAirHandlerPatches
    {
        // Fix 1: OnExitTrigger guaranteed NRE
        [HarmonyPatch("OnExitTrigger")]
        [HarmonyPrefix]
        public static bool OnExitTrigger_FixNRE(CharacterAirHandler __instance)
        {
            var airZoneField = Traverse.Create(__instance).Field("airZone");
            var airZone = airZoneField.GetValue<AirZone>();

            // Check O2 level BEFORE clearing the reference
            if (airZone != null)
            {
                airZone.UpdateO2Levels();
                if (airZone.current_o2Level < __instance.OnExitO2TriggerTreshold)
                {
                    __instance.OnExitAirZone?.Invoke();
                }
            }

            // Now clear the reference
            airZoneField.SetValue(null);
            return false; // Skip original
        }

        // Fix 3: OnStayTrigger null check for airZone
        [HarmonyPatch("OnStayTrigger")]
        [HarmonyPrefix]
        public static bool OnStayTrigger_NullSafe(CharacterAirHandler __instance)
        {
            if (__instance.airmaskState) return false;

            var airZone = Traverse.Create(__instance).Field("airZone").GetValue<AirZone>();
            if (airZone == null) return false;

            airZone.UpdateO2Levels();
            __instance.airVolume += airZone.current_o2Level * Time.deltaTime;
            __instance.airVolume = Mathf.Clamp(__instance.airVolume, 0f, 100f);

            return false; // Skip original
        }

        // Fix 4: Awake null safety for offline/singleplayer
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        public static bool Awake_NullSafe(CharacterAirHandler __instance)
        {
            var pv = __instance.gameObject.GetComponent<Photon.Pun.PhotonView>();
            Traverse.Create(__instance).Field("photonView").SetValue(pv);

            __instance.airVolume = 30f;
            __instance.full = __instance.maskAirVolume;
            __instance.regenRate = 30f;

            // In offline mode, Owner can be null
            if (pv != null && pv.Owner != null &&
                Photon.Pun.PhotonNetwork.LocalPlayer != null &&
                pv.Owner.UserId != Photon.Pun.PhotonNetwork.LocalPlayer.UserId)
            {
                __instance.enabled = false;
            }

            return false; // Skip original
        }
    }
}
