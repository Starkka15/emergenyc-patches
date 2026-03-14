using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for RCC_PhotonNetwork:
    /// 1. FixedUpdate: photonView.Owner.ActorNumber accessed without null check - Owner can be
    ///    null in offline mode or when ownership hasn't been assigned yet, crashing the entire
    ///    vehicle networking system.
    /// 2. LateUpdate: Same null-dereference issue with photonView.Owner.ActorNumber.
    /// </summary>
    [HarmonyPatch(typeof(RCC_PhotonNetwork))]
    public static class RCC_PhotonNetworkPatches
    {
        private static bool _loggedFixedSkip;
        private static bool _loggedLateSkip;

        // Fix 1: Guard FixedUpdate against null Owner / LocalPlayer
        [HarmonyPatch(nameof(RCC_PhotonNetwork.FixedUpdate))]
        [HarmonyPrefix]
        public static bool FixedUpdate_NullOwnerGuard(RCC_PhotonNetwork __instance)
        {
            if (__instance.photonView.Owner == null || PhotonNetwork.LocalPlayer == null)
            {
                if (!_loggedFixedSkip)
                {
                    _loggedFixedSkip = true;
                    Plugin.Log(
                        $"[RCC_PhotonNetwork] FixedUpdate SKIPPED: Owner={(__instance.photonView.Owner != null ? "OK" : "NULL")}" +
                        $" LocalPlayer={(PhotonNetwork.LocalPlayer != null ? "OK" : "NULL")}" +
                        $" obj={__instance.gameObject.name}");
                }
                return false;
            }
            return true;
        }

        // Fix 2: Guard LateUpdate against null Owner / LocalPlayer
        [HarmonyPatch(nameof(RCC_PhotonNetwork.LateUpdate))]
        [HarmonyPrefix]
        public static bool LateUpdate_NullOwnerGuard(RCC_PhotonNetwork __instance)
        {
            if (__instance.photonView.Owner == null || PhotonNetwork.LocalPlayer == null)
            {
                if (!_loggedLateSkip)
                {
                    _loggedLateSkip = true;
                    Plugin.Log(
                        $"[RCC_PhotonNetwork] LateUpdate SKIPPED: Owner={(__instance.photonView.Owner != null ? "OK" : "NULL")}" +
                        $" LocalPlayer={(PhotonNetwork.LocalPlayer != null ? "OK" : "NULL")}" +
                        $" obj={__instance.gameObject.name}");
                }
                return false;
            }
            return true;
        }
    }
}
