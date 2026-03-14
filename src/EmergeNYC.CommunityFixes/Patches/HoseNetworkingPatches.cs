using HarmonyLib;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for HoseNetworking:
    /// 1. Missing null check on attachPoint before calling RPC methods
    /// </summary>
    [HarmonyPatch(typeof(HoseNetworking))]
    public static class HoseNetworkingPatches
    {
        // Fix 1: Null-safe OnHoseAttached
        [HarmonyPatch(nameof(HoseNetworking.OnHoseAttached))]
        [HarmonyPrefix]
        public static bool OnHoseAttached_NullSafe(HoseNetworking __instance, MountPoint mountPoint)
        {
            if (mountPoint == null || __instance.photonView == null)
                return false;
            return true; // Let original run
        }

        // Fix 3: Null-safe OnAttachHoseCallback
        [HarmonyPatch("OnAttachHoseCallback")]
        [HarmonyPrefix]
        public static bool OnAttachHoseCallback_NullSafe(HoseNetworking __instance, float mountPointID)
        {
            var attachPoint = Traverse.Create(__instance).Field("attachPoint").GetValue<HoseAttachPoint>();
            if (attachPoint == null)
                return false;

            MountPoint mp = MountPoint.GetByID(mountPointID);
            if (mp == null)
                return false;

            return true; // Let original run
        }
    }
}
