using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for the Singleton pattern:
    /// 1. FindObjectOfType called TWICE when multiple instances exist (line 23 + line 24)
    ///    - First call finds the instance, second call finds ALL instances just to check count
    ///    - FindObjectsOfType is extremely expensive
    /// 2. OnDestroy sets applicationIsQuitting = true, but this prevents re-creation after
    ///    scene transitions if the singleton was in the old scene
    /// </summary>

    // Note: We can't easily patch generic types with Harmony, so we'll patch the most
    // commonly accessed singleton: Singleton<PUNGlobals>
    [HarmonyPatch(typeof(Singleton<PUNGlobals>), "OnDestroy")]
    public static class SingletonPUNGlobalsOnDestroyPatch
    {
        // Fix: Don't set applicationIsQuitting if we're just changing scenes
        public static bool Prefix(Singleton<PUNGlobals> __instance)
        {
            bool isPlaying = Application.isPlaying;
            Plugin.Log(
                $"[Singleton<PUNGlobals>] OnDestroy called: isPlaying={isPlaying}" +
                $" letting original run={!isPlaying}");

            if (!isPlaying)
            {
                // Application is quitting
                return true; // Let original run
            }

            // During scene transitions, don't set the flag
            // This prevents the singleton from being inaccessible after scene load
            return false;
        }
    }
}
