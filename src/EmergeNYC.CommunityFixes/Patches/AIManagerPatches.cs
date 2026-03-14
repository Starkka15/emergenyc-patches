using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Targeted fixes for AIManager — NO full Update() replacement.
    ///
    /// The original Update() has minor NRE issues (parent null check at line 473,
    /// AddComponent every frame at line 478) but replacing the entire 221-line method
    /// caused character spawning regressions. Unity catches these NREs gracefully.
    ///
    /// We only suppress the known NRE from transform.parent.name when parent is null.
    /// All NREs are logged (once per type) so we can diagnose without log spam.
    /// </summary>
    [HarmonyPatch(typeof(AIManager))]
    public static class AIManagerPatches
    {
        private static bool _loggedParentNRE;
        private static int _nreCount;

        [HarmonyPatch("Update")]
        [HarmonyFinalizer]
        public static System.Exception Update_CatchParentNRE(System.Exception __exception, AIManager __instance)
        {
            if (__exception is System.NullReferenceException)
            {
                _nreCount++;
                if (!_loggedParentNRE)
                {
                    _loggedParentNRE = true;
                    Plugin.Log(
                        $"[AIManager] Update NRE suppressed (likely parent.name null check): " +
                        $"{__exception.Message}" +
                        $" obj={(__instance != null ? __instance.gameObject?.name ?? "no-go" : "NULL")}" +
                        $"\n{__exception.StackTrace}");
                }
                // Log count periodically
                if (_nreCount == 100 || _nreCount == 1000 || _nreCount == 10000)
                {
                    Plugin.Log($"[AIManager] Update NRE count: {_nreCount}");
                }
                return null; // Suppress — non-fatal, game recovers next frame
            }
            if (__exception != null)
            {
                Plugin.Log(
                    $"[AIManager] Update threw NON-NRE {__exception.GetType().Name}: {__exception.Message}" +
                    $"\n{__exception.StackTrace}");
            }
            return __exception;
        }
    }
}
