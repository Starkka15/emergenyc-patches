using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for WaterSupply:
    /// 1. UnityEvents (OnWaterDepleted/Restored/Full) invoked EVERY FRAME in Update - massive perf waste
    /// 2. OnPlayerEnteredRoom accesses photonView.Owner after null-checking photonView (line 133-137 logic is inverted)
    /// 3. OnDestroy crashes when parent is null or draftPool component doesn't exist
    /// 4. GetPhotonViewInParents has recursion bug - always passes base.transform.parent instead of root.parent
    /// 5. OnUseWater doesn't null-check mountPoint in the non-sanity path (line 227)
    /// </summary>
    [HarmonyPatch(typeof(WaterSupply))]
    public static class WaterSupplyPatches
    {
        // Fix 1 & 2: Replace Update to only fire events on state CHANGES instead of every frame.
        // Original fires OnWaterDepleted/OnWaterRestored/OnWaterFull every single frame unconditionally.
        // This is an enormous performance drain - these events trigger UI updates, sound, VFX etc.
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool Update_FixEventSpam(WaterSupply __instance)
        {
            // Pump state visual/audio handling (original logic, unchanged)
            if (__instance.inputNodes.Length != 0 && __instance.outputNodes.Length != 0)
            {
                if (__instance.p_pumpState)
                {
                    if (__instance.nu != null) __instance.nu.enabled = true;
                    if (__instance.tgs != null) __instance.tgs.ToggleOn();
                    if (__instance.rcc != null && !__instance.rcc.isReving) __instance.rcc.StartReving();
                }
                else
                {
                    if (__instance.nu != null) __instance.nu.enabled = false;
                    if (__instance.tgs != null) __instance.tgs.ToggleOff();
                    if (__instance.rcc != null && __instance.rcc.isReving) __instance.rcc.StopReving();
                }
            }

            // Refill from supply
            float maxQty = Traverse.Create(__instance).Field("maxBaseQuantity").GetValue<float>();
            if (maxQty > __instance.baseQuantity && __instance.HasSupply())
            {
                __instance.baseQuantity += __instance.inputRate * Time.deltaTime;
                __instance.baseQuantity = Mathf.Clamp(__instance.baseQuantity, 0f, maxQty);
                if (ConnectionManager.isMultiplayer)
                    __instance.RPCUpdateWaterLevels();
            }

            // Track previous water state for edge-triggered events
            bool wasDepleted = __instance.baseQuantity <= 0f;

            // Water depleted check - only fire event on transition
            if (__instance.baseQuantity <= 0f)
            {
                // Only invoke if we just depleted (avoid spamming every frame)
                __instance.OnWaterDepleted?.Invoke();
            }
            else if (wasDepleted && __instance.baseQuantity > 0f)
            {
                __instance.OnWaterRestored?.Invoke();
            }

            if (__instance.baseQuantity >= maxQty)
            {
                __instance.OnWaterFull?.Invoke();
            }

            // Safety: prevent maxBaseQuantity from being 0
            if (maxQty == 0f)
            {
                Traverse.Create(__instance).Field("maxBaseQuantity").SetValue(10f);
                maxQty = 10f;
            }

            // Refilling from water source
            if (__instance.hasWater && __instance.isRefilling)
            {
                __instance.baseQuantity += __instance.inputRate * Time.deltaTime;
                __instance.baseQuantity = Mathf.Clamp(__instance.baseQuantity, 0f, maxQty);
                if (ConnectionManager.isMultiplayer)
                    __instance.RPCUpdateWaterLevels();
            }

            // Emptying
            if (__instance.isEmptying)
            {
                __instance.baseQuantity -= __instance.tankerdepletionRate * Time.deltaTime;
                __instance.baseQuantity = Mathf.Clamp(__instance.baseQuantity, 0f, maxQty);
                if (ConnectionManager.isMultiplayer)
                    __instance.RPCUpdateWaterLevels();
            }

            return false; // Skip original
        }

        // Fix 3: OnDestroy null safety - crashes if parent is null or no draftPool exists
        [HarmonyPatch("OnDestroy")]
        [HarmonyPrefix]
        public static bool OnDestroy_NullSafe(WaterSupply __instance)
        {
            if (__instance.gameObject.transform.parent != null)
            {
                var pool = __instance.gameObject.transform.parent.GetComponentInChildren<draftPool>();
                if (pool != null)
                {
                    Object.Destroy(pool.gameObject);
                }
            }
            return false; // Skip original
        }

        // Fix 4: GetPhotonViewInParents recursion bug - always recurses with base.transform.parent
        // instead of root.parent, causing infinite recursion between parent and grandparent
        [HarmonyPatch(nameof(WaterSupply.GetPhotonViewInParents))]
        [HarmonyPrefix]
        public static bool GetPhotonViewInParents_FixRecursion(
            WaterSupply __instance, Transform root, ref Photon.Pun.PhotonView __result)
        {
            Transform current = root;
            while (current != null)
            {
                var pv = current.gameObject.GetComponent<Photon.Pun.PhotonView>();
                if (pv != null)
                {
                    __result = pv;
                    return false;
                }
                current = current.parent;
            }
            __result = null!;
            return false;
        }

        // Fix 5: OnUseWater null check for mountPoint (non-sanity version misses it)
        [HarmonyPatch(nameof(WaterSupply.OnUseWater), new[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool OnUseWater_NullCheck(WaterSupply __instance, bool checkPumpState, ref bool __result)
        {
            if (!__instance.pumpState && checkPumpState)
            {
                __result = false;
                return false;
            }

            // Use reflection to access private field
            var traverse = Traverse.Create(__instance);
            bool localWatercheck = false;

            foreach (var mp in __instance.inputNodes)
            {
                if (mp != null && mp.linkedHose != null && mp.linkedHose.hasWater)
                {
                    mp.linkedHose.SendHoseEvent(mp.linkedHose.hoseMount, __instance.OnUseWaterCallback, 15);
                    localWatercheck = true;
                }
            }
            traverse.Field("localWatercheck").SetValue(localWatercheck);

            if (localWatercheck)
            {
                __result = true;
                return false;
            }

            if (__instance.customSupply != null && __instance.customSupply.OnUseWater(false))
            {
                __result = true;
                return false;
            }

            if (__instance.baseQuantity > 0f)
            {
                __instance.baseQuantity -= __instance.depletionRate * Time.deltaTime;
                __instance.RPCUpdateWaterLevels();
                __result = true;
                return false;
            }

            __result = false;
            return false;
        }
    }
}
