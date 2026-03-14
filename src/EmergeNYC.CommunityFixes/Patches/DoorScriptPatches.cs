using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for DoorScript:
    /// 1. OnPlayerEnteredRoom calls OpenDoorNetworked()/CloseDoorNetworked() as local methods
    ///    instead of RPCs. The master client updates its own door state but never sends it to
    ///    the joining player. Compare with OpenDoor()/CloseDoor()/ToggleDoor() which correctly
    ///    use photonView.RPC(..., RpcTarget.All). Fix: send the RPC targeted at the new player
    ///    so they receive the current door state on join.
    /// </summary>
    [HarmonyPatch(typeof(DoorScript))]
    public static class DoorScriptPatches
    {
        // Fix 1: Sync door state to newly joined player via targeted RPC
        [HarmonyPatch(nameof(DoorScript.OnPlayerEnteredRoom))]
        [HarmonyPrefix]
        public static bool OnPlayerEnteredRoom_SendRPC(DoorScript __instance, Player newPlayer)
        {
            if (!PhotonNetwork.IsMasterClient)
                return false; // Skip original

            bool opened = Traverse.Create(__instance).Field("Opened").GetValue<bool>();

            if (opened)
            {
                __instance.photonView.RPC("OpenDoorNetworked", newPlayer);
            }
            else
            {
                __instance.photonView.RPC("CloseDoorNetworked", newPlayer);
            }

            return false; // Skip original
        }
    }
}
