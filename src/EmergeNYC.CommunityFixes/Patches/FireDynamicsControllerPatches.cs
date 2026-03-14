using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for FireDynamicsController:
    /// 1. Double-registration in static list (Awake + OnEnable both call AddController)
    /// 2. Null reference in RemoveController when list is null
    /// 3. Null reference in GetControllerAtPosition when list is null/empty
    /// 4. Division by zero in GetAvarageSmokeState when gridIgnition.Count is 0
    /// 5. Null reference in GetControllerState when controllers array is empty (div by zero)
    /// 6. Debug key (Alpha9) left in Update fires random ignition in release builds
    /// </summary>
    [HarmonyPatch(typeof(FireDynamicsController))]
    public static class FireDynamicsControllerPatches
    {
        // Fix 1: Prevent duplicate entries in the static controller list.
        // Awake() calls AddController, then OnEnable() calls it again = double registration.
        // This causes double-processing of fire dynamics and potential issues on removal.
        [HarmonyPatch(nameof(FireDynamicsController.OnEnable))]
        [HarmonyPrefix]
        public static bool OnEnable_PreventDuplicateAdd(FireDynamicsController __instance)
        {
            if (FireDynamicsController.fireDynamicControllers == null)
            {
                FireDynamicsController.fireDynamicControllers = new List<FireDynamicsController>();
            }
            if (!FireDynamicsController.fireDynamicControllers.Contains(__instance))
            {
                FireDynamicsController.fireDynamicControllers.Add(__instance);
            }
            return false; // Skip original
        }

        // Fix 2: Null-safe RemoveController to prevent NRE when list hasn't been initialized
        [HarmonyPatch(nameof(FireDynamicsController.OnDisable))]
        [HarmonyPrefix]
        public static bool OnDisable_NullSafeRemove(FireDynamicsController __instance)
        {
            FireDynamicsController.fireDynamicControllers?.Remove(__instance);
            return false; // Skip original
        }

        [HarmonyPatch(nameof(FireDynamicsController.OnDestroy))]
        [HarmonyPrefix]
        public static bool OnDestroy_NullSafeRemove(FireDynamicsController __instance)
        {
            FireDynamicsController.fireDynamicControllers?.Remove(__instance);
            return false; // Skip original
        }

        // Fix 3: Null/empty-safe GetControllerAtPosition
        [HarmonyPatch(nameof(FireDynamicsController.GetControllerAtPosition))]
        [HarmonyPrefix]
        public static bool GetControllerAtPosition_NullSafe(Vector3 position, ref FireDynamicsController __result)
        {
            if (FireDynamicsController.fireDynamicControllers == null ||
                FireDynamicsController.fireDynamicControllers.Count == 0)
            {
                __result = null!;
                return false;
            }
            return true; // Let original run if list is valid
        }

        // Fix 4: Division by zero in GetAvarageSmokeState when gridIgnition is empty
        [HarmonyPatch(nameof(FireDynamicsController.GetAvarageSmokeState))]
        [HarmonyPrefix]
        public static bool GetAvarageSmokeState_DivByZeroFix(FireDynamicsController __instance, ref float __result)
        {
            if (__instance.gridIgnition == null || __instance.gridIgnition.Count == 0)
            {
                __result = 0f;
                return false;
            }

            float total = 0f;
            foreach (var item in __instance.gridIgnition)
            {
                if (item != null && item.controller != null)
                {
                    total += item.controller.GetSmokeState();
                }
            }
            __result = total / __instance.gridIgnition.Count;
            return false;
        }

        // Fix 5: Division by zero in GetControllerState when controllers array is empty
        [HarmonyPatch(nameof(FireDynamicsController.GetControllerState))]
        [HarmonyPrefix]
        public static bool GetControllerState_DivByZeroFix(
            FireDynamicsController __instance,
            FireDynamicsController.ParticleType controllerType,
            FireGridIgnition[] controllers,
            bool ignitedOnly,
            ref float __result)
        {
            if (controllers == null || controllers.Length == 0)
            {
                __result = 0f;
                return false;
            }
            return true; // Let original run
        }

        // Fix 6: Remove debug key (Alpha9) that triggers random fire ignition
        // This is clearly a dev testing key that was left in the release build
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool Update_RemoveDebugKey()
        {
            return false; // Skip the entire Update - it only checks for debug key Alpha9
        }
    }
}
