using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for ABLoadManager:
    /// 1. Unload() crashes with NRE after logging error: finds null ABMemoryRef, logs the
    ///    error, but then tries to call .UnloadAsset() on the null reference anyway (missing return)
    /// 2. ForceUnloadBundles crashes if any abCreateRequest is null or assetBundle is null
    /// 3. OnRequestABTex logic is inverted: starts coroutine when pool is FULL instead of
    ///    when it has capacity. The else branch adds to requests but never starts loading.
    /// </summary>
    [HarmonyPatch(typeof(ABLoadManager))]
    public static class ABLoadManagerPatches
    {
        // Fix 1: Missing return after null check in Unload
        [HarmonyPatch(nameof(ABLoadManager.Unload))]
        [HarmonyPrefix]
        public static bool Unload_NullSafe(ABLoadManager __instance, ABLoadManager.ABAssetRequest asset)
        {
            if (asset == null) return false;

            var memRef = __instance.abMemoryRef.Find(p => p.abName == asset.assetBundleName);
            if (memRef == null)
            {
                Debug.LogError("Could not find asset bundle: " + asset.assetBundleName + " in memory ref");
                return false; // Fix: don't continue with null memRef
            }

            memRef.UnloadAsset(asset, __instance.cacheInMem);
            return false; // Skip original
        }

        // Fix 2: ForceUnloadBundles null safety
        [HarmonyPatch(nameof(ABLoadManager.ForceUnloadBundles))]
        [HarmonyPrefix]
        public static bool ForceUnloadBundles_NullSafe(ABLoadManager __instance)
        {
            foreach (var item in __instance.abMemoryRef)
            {
                if (item?.abCreateRequest?.assetBundle != null)
                {
                    item.abCreateRequest.assetBundle.Unload(false);
                }
            }
            Resources.UnloadUnusedAssets();
            return false; // Skip original
        }

        // Fix 3: OnRequestABTex inverted logic
        [HarmonyPatch(nameof(ABLoadManager.OnRequestABTex))]
        [HarmonyPrefix]
        public static bool OnRequestABTex_FixLogic(ABLoadManager __instance, ABTextureLoader loadRequest)
        {
            var loadingABs = Traverse.Create(__instance).Field("mempoolLoadingABs")
                .GetValue<System.Collections.Generic.List<ABTextureLoader>>();
            var loadRequests = Traverse.Create(__instance).Field("mempoolLoadRequests")
                .GetValue<System.Collections.Generic.List<ABTextureLoader>>();

            // Fix: Start loading when we have capacity, queue when full
            if (loadingABs.Count < __instance.maxConcurrenctTexAbs)
            {
                loadRequests.Add(loadRequest);
                loadRequest.StartCoroutine(loadRequest.ABTexureLoad());
                loadingABs.Add(loadRequest);
            }
            else
            {
                // Queue for later - Update() will process when capacity frees up
                loadRequests.Add(loadRequest);
            }

            return false; // Skip original
        }
    }
}
