using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for OnMountedDispatch:
    /// 1. Update() line 48: "if (!rigid && lastState)" then accesses rigid.drag - NRE.
    ///    The condition !rigid means rigid IS null, so rigid.drag crashes.
    ///    The intent was likely "if (rigid != null && !lastState)" to reset physics when detached.
    /// </summary>
    [HarmonyPatch(typeof(OnMountedDispatch))]
    public static class OnMountedDispatchPatches
    {
        private static bool _loggedOnce;

        // Fix 1: Replace Update to fix the inverted null check
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool Update_FixNullCheck(OnMountedDispatch __instance)
        {
            var lastState = Traverse.Create(__instance).Field("lastState").GetValue<bool>();

            if (!_loggedOnce && lastState)
            {
                _loggedOnce = true;
                Plugin.Log(
                    $"[OnMountedDispatch] Update running: lastState={lastState}" +
                    $" rigid={(__instance.rigid != null ? __instance.rigid.name : "NULL")}" +
                    $" freezerigids={__instance.freezerigids}" +
                    $" obj={__instance.gameObject.name}");
            }

            if (lastState)
            {
                __instance.OnMountAttachedUpdate.Invoke();
            }

            if (__instance.rigid != null && lastState && __instance.freezerigids)
            {
                __instance.rigid.drag = 999f;
                __instance.rigid.angularDrag = 999f;
                __instance.rigid.constraints = RigidbodyConstraints.FreezeAll;
            }

            // Fix: Original had "if (!rigid && lastState)" which NREs.
            // Correct logic: reset physics when rigid exists and NOT in mounted state
            if (__instance.rigid != null && !lastState)
            {
                __instance.rigid.drag = 0f;
                __instance.rigid.angularDrag = 0.05f;
                __instance.rigid.constraints = RigidbodyConstraints.None;
            }

            return false; // Skip original
        }
    }
}
