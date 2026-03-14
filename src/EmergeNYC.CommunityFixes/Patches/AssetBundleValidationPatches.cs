using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Bypasses Unity Addressables CRC and size validation on asset bundles.
    /// This allows modified/rebuilt bundles (e.g. texture replacements) to load
    /// without needing CheatEngine to patch expected sizes at runtime.
    ///
    /// The game's catalog.json stores m_Crc, m_BundleSize, and m_Hash for each
    /// bundle. Unity's Addressables system validates these when loading. By
    /// patching the getters on AssetBundleRequestOptions and the low-level
    /// AssetBundle.LoadFromFile* calls, all validation is bypassed.
    ///
    /// User workflow becomes: modify bundle with UABE -> drop in place -> play.
    /// </summary>

    // --- Addressables-level patches (AssetBundleRequestOptions) ---

    // Patch 1: CRC getter -> always return 0 (CRC=0 means "skip check" in Unity)
    [HarmonyPatch(typeof(AssetBundleRequestOptions))]
    public static class AssetBundleRequestOptionsCrcPatch
    {
        [HarmonyPatch("get_Crc")]
        [HarmonyPostfix]
        public static void Get_Crc(ref uint __result)
        {
            __result = 0;
        }
    }

    // Patch 2: UseCrcForCachedBundle -> always false
    [HarmonyPatch(typeof(AssetBundleRequestOptions))]
    public static class AssetBundleRequestOptionsUseCrcPatch
    {
        [HarmonyPatch("get_UseCrcForCachedBundle")]
        [HarmonyPostfix]
        public static void Get_UseCrcForCachedBundle(ref bool __result)
        {
            __result = false;
        }
    }

    // Patch 3: BundleSize getter -> return actual file size if bundle exists on disk
    // This prevents size-mismatch rejections for rebuilt bundles
    [HarmonyPatch(typeof(AssetBundleRequestOptions))]
    public static class AssetBundleRequestOptionsBundleSizePatch
    {
        [HarmonyPatch("get_BundleSize")]
        [HarmonyPostfix]
        public static void Get_BundleSize(AssetBundleRequestOptions __instance, ref long __result)
        {
            if (__result <= 0 || string.IsNullOrEmpty(__instance.BundleName))
                return;

            try
            {
                // Try to find the actual bundle file and return its real size
                string bundleDir = Path.Combine(Application.dataPath,
                    "StreamingAssets", "aa", "StandaloneWindows64");
                string bundlePath = Path.Combine(bundleDir, __instance.BundleName);

                if (File.Exists(bundlePath))
                {
                    long actualSize = new FileInfo(bundlePath).Length;
                    if (actualSize != __result)
                    {
                        Plugin.Logger.LogInfo(
                            $"[BundleBypass] Size override: {__instance.BundleName} " +
                            $"catalog={__result} actual={actualSize}");
                        __result = actualSize;
                    }
                }
            }
            catch
            {
                // Silently ignore - don't break loading over a size check bypass
            }
        }
    }

    // --- Low-level AssetBundle patches (belt and suspenders) ---

    // Patch 4: AssetBundle.LoadFromFile(string, uint) -> crc = 0
    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadFromFile),
        new Type[] { typeof(string), typeof(uint) })]
    public static class LoadFromFileCrcPatch
    {
        public static void Prefix(ref uint crc)
        {
            crc = 0;
        }
    }

    // Patch 5: AssetBundle.LoadFromFile(string, uint, ulong) -> crc = 0
    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadFromFile),
        new Type[] { typeof(string), typeof(uint), typeof(ulong) })]
    public static class LoadFromFile3Patch
    {
        public static void Prefix(ref uint crc)
        {
            crc = 0;
        }
    }

    // Patch 6: AssetBundle.LoadFromFileAsync(string, uint) -> crc = 0
    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadFromFileAsync),
        new Type[] { typeof(string), typeof(uint) })]
    public static class LoadFromFileAsyncCrcPatch
    {
        public static void Prefix(ref uint crc)
        {
            crc = 0;
        }
    }

    // Patch 7: AssetBundle.LoadFromFileAsync(string, uint, ulong) -> crc = 0
    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadFromFileAsync),
        new Type[] { typeof(string), typeof(uint), typeof(ulong) })]
    public static class LoadFromFileAsync3Patch
    {
        public static void Prefix(ref uint crc)
        {
            crc = 0;
        }
    }

    // Patch 8: UnityWebRequestAssetBundle.GetAssetBundle(string, uint) -> crc = 0
    // In case any bundles are loaded via web request path
    [HarmonyPatch(typeof(UnityWebRequestAssetBundle), nameof(UnityWebRequestAssetBundle.GetAssetBundle),
        new Type[] { typeof(string), typeof(uint) })]
    public static class WebRequestAssetBundleCrcPatch
    {
        public static void Prefix(ref uint crc)
        {
            crc = 0;
        }
    }
}
