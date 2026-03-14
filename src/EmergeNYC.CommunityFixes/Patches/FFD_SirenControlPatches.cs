using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for FFD_SirenControl:
    /// 1. UpdateInput uses Input.GetButton(Horn_Key) instead of Input.GetButtonDown(Horn_Key).
    ///    GetButton returns true EVERY FRAME while the button is held, so OnHornKey() and its
    ///    RPC are called every single frame the horn button is pressed. This floods the Photon
    ///    network with RPC calls and spams the siren state machine.
    ///    The horn-off path already uses GetButtonUp (fires once on release), so only the
    ///    horn-on path needs fixing: change GetButton to GetButtonDown so the RPC fires once.
    /// </summary>
    [HarmonyPatch(typeof(FFD_SirenControl))]
    public static class FFD_SirenControlPatches
    {
        // Fix 1: UpdateInput - prevent horn RPC spam by using GetButtonDown instead of GetButton
        [HarmonyPatch("UpdateInput")]
        [HarmonyPrefix]
        public static bool UpdateInput_FixHornRPCSpam(FFD_SirenControl __instance)
        {
            var carbhv = Traverse.Create(__instance).Field("carbhv").GetValue<RCCEnterExitCar>();
            var Horn_Key = Traverse.Create(__instance).Field("Horn_Key").GetValue<string>();
            var Man_Key = Traverse.Create(__instance).Field("Man_Key").GetValue<string>();
            var Wail_Key = Traverse.Create(__instance).Field("Wail_Key").GetValue<string>();
            var Yelp_Key = Traverse.Create(__instance).Field("Yelp_Key").GetValue<string>();
            var Prty_Key = Traverse.Create(__instance).Field("Prty_Key").GetValue<string>();
            var Rumble_Key = Traverse.Create(__instance).Field("Rumble_Key").GetValue<string>();

            if (carbhv != null && carbhv.isPlayerSpectator)
            {
                __instance.sendSyncRPC = true;
            }

            if (Input.GetButtonDown(Rumble_Key))
            {
                __instance.OnRumbleKey();
                __instance.photonView.RPC("OnRumbleKey", RpcTarget.Others);
            }

            if (__instance.Horn != null)
            {
                // FIX: Use GetButtonDown instead of GetButton to fire the horn RPC only once
                // on press, not every frame while held
                if (Input.GetButtonDown(Horn_Key) && !Input.GetButton(Man_Key))
                {
                    __instance.OnHornKey();
                    __instance.photonView.RPC("OnHornKey", RpcTarget.Others);
                }

                if (Input.GetButtonUp(Horn_Key) && !Input.GetButton(Man_Key))
                {
                    __instance.OnHornKeyUp();
                    __instance.photonView.RPC("OnHornKeyUp", RpcTarget.Others);
                }
            }

            if (Input.GetButtonDown(Wail_Key))
            {
                __instance.OnWailKeyDn();
                __instance.photonView.RPC("OnWailKeyDn", RpcTarget.Others);
            }

            if (Input.GetButtonDown(Yelp_Key))
            {
                __instance.OnYelpKeyDn();
                __instance.photonView.RPC("OnYelpKeyDn", RpcTarget.Others);
            }

            if (Input.GetButtonDown(Prty_Key) && __instance.Prty != null)
            {
                __instance.OnPrtyKeyDn();
                __instance.photonView.RPC("OnPrtyKeyDn", RpcTarget.Others);
            }

            if (Input.GetButtonDown(Man_Key))
            {
                __instance.OnManualKey();
                __instance.photonView.RPC("OnManualKey", RpcTarget.Others);
            }

            if (Input.GetButtonUp(Man_Key))
            {
                __instance.OnManKeyUp();
                __instance.photonView.RPC("OnManKeyUp", RpcTarget.Others);
            }

            return false; // Skip original
        }
    }
}
