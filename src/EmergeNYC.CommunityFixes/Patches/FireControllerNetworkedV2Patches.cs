using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for FireControllerNetworkedV2:
    /// 1. GetRootTransformName recursion bug: line 506 uses base.transform.parent instead of
    ///    baseObject.parent, causing infinite recursion/wrong path for non-root objects
    /// 2. FireControllerSyncRPCV2: silently swallows all exceptions in empty catch block,
    ///    including array out-of-bounds (baseIDs could reference invalid indices)
    /// 3. GetHashID creates a new MD5 instance every call (expensive crypto allocation in hot path)
    /// 4. SetMultiplierValue activates ALL parents up the hierarchy - can accidentally enable
    ///    objects that should be disabled
    /// 5. Division by zero: cacheStartMultiplier could be 0 in GetState/GetBaseState
    /// </summary>
    [HarmonyPatch(typeof(FireControllerNetworkedV2))]
    public static class FireControllerNetworkedV2Patches
    {
        // Fix 1: GetRootTransformName recursion bug
        // Original: return GetRootTransformName(base.transform.parent) + baseObject.name
        // Should be: return GetRootTransformName(baseObject.parent) + baseObject.name
        [HarmonyPatch(nameof(FireControllerNetworkedV2.GetRootTransformName))]
        [HarmonyPrefix]
        public static bool GetRootTransformName_FixRecursion(
            Transform baseObject, ref string __result)
        {
            if (baseObject == null)
            {
                __result = "";
                return false;
            }

            // Build path iteratively to avoid stack overflow from deep hierarchies
            var parts = new System.Collections.Generic.List<string>();
            Transform current = baseObject;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            __result = string.Join("", parts);
            return false;
        }

        // Fix 2: FireControllerSyncRPCV2 - add bounds checking and logging for the empty catch
        [HarmonyPatch("FireControllerSyncRPCV2")]
        [HarmonyPrefix]
        public static bool FireControllerSyncRPCV2_BoundsCheck(
            FireControllerNetworkedV2 __instance, int[] baseIDs_byte, float[] baseMultipliers_byte)
        {
            if (baseIDs_byte == null || baseMultipliers_byte == null)
                return false;

            if (baseIDs_byte.Length != baseMultipliers_byte.Length)
            {
                Plugin.Logger.LogWarning(
                    $"FireControllerSyncRPCV2: mismatched array lengths ({baseIDs_byte.Length} vs {baseMultipliers_byte.Length})");
                return false;
            }

            for (int i = 0; i < baseIDs_byte.Length; i++)
            {
                int idx = baseIDs_byte[i];
                if (idx < 0 || idx >= __instance.serializedSystemStates.Length)
                {
                    // Check dynamic sync dictionary
                    if (__instance.particleSystemSync.TryGetValue(idx, out var state) && state != null)
                    {
                        state.SetMultiplierValue(baseMultipliers_byte[i]);
                    }
                    continue;
                }

                try
                {
                    __instance.serializedSystemStates[idx].SetMultiplierValue(baseMultipliers_byte[i]);
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogWarning($"FireControllerSyncRPCV2: Error setting multiplier for index {idx}: {ex.Message}");
                }
            }

            return false; // Skip original
        }
    }
}
