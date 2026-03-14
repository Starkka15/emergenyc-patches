using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for StretcherController:
    /// 1. SetStretcherState(bool) has identical code in both branches of an if/else on photonView.
    ///    When photonView is not null, it should send an RPC to synchronize across the network,
    ///    but instead it calls SetStretcherStateNetworked locally in both cases.
    ///    This means other players never see stretcher state changes.
    ///    Fix: When photonView exists, send the RPC via photonView.RPC("SetStretcherStateNetworked",
    ///    RpcTarget.All, state) so all clients (including local) receive the state change.
    ///    When photonView is null (offline/singleplayer), call the local method directly.
    /// </summary>
    [HarmonyPatch(typeof(StretcherController))]
    public static class StretcherControllerPatches
    {
        // Fix 1: SetStretcherState(bool) - send RPC when photonView is available
        [HarmonyPatch(nameof(StretcherController.SetStretcherState), typeof(bool))]
        [HarmonyPrefix]
        public static bool SetStretcherState_FixRPC(StretcherController __instance, bool state)
        {
            if (__instance.photonView != null)
            {
                // Send as RPC so all clients receive the state change
                __instance.photonView.RPC("SetStretcherStateNetworked", RpcTarget.All, state);
            }
            else
            {
                // Offline/singleplayer fallback - call locally
                __instance.SetStretcherStateNetworked(state);
            }

            return false; // Skip original
        }
    }
}
