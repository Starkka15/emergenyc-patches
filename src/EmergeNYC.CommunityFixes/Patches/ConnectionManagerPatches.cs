using HarmonyLib;
using Photon.Pun;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for ConnectionManager:
    /// 1. Singleton not set in Awake - Instance field is never assigned
    /// 2. OnJoinedRoom called twice in CreateRoom coroutine (once from Photon callback, once manually)
    /// 3. OnNetworkReady event never cleaned up (static event = permanent subscription leak)
    /// 4. CreateRoom random name collision potential (small range 0-10000)
    /// </summary>
    [HarmonyPatch(typeof(ConnectionManager))]
    public static class ConnectionManagerPatches
    {
        // Fix 1: Set singleton instance in Awake
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        public static void Awake_SetSingleton(ConnectionManager __instance)
        {
            if (ConnectionManager.Instance == null)
            {
                ConnectionManager.Instance = __instance;
            }
        }

        // Fix 2: Prevent double OnJoinedRoom execution in CreateRoom coroutine.
        // The coroutine manually calls OnJoinedRoom() after CreateRoom returns true,
        // but Photon also fires OnJoinedRoom callback when the room is actually joined.
        // The OnJoinedRoomExecuted flag is supposed to guard this, but CreateRoom
        // calls it before the flag is set, and then Photon calls it again.
        [HarmonyPatch(nameof(ConnectionManager.OnJoinedRoom))]
        [HarmonyPrefix]
        public static bool OnJoinedRoom_PreventDoubleExec(ConnectionManager __instance)
        {
            var executed = Traverse.Create(__instance).Field("OnJoinedRoomExecuted");
            bool alreadyExecuted = executed.GetValue<bool>();
            Plugin.Log(
                $"[ConnectionManager] OnJoinedRoom called: OnJoinedRoomExecuted={alreadyExecuted}" +
                $" skipping={alreadyExecuted}");
            if (alreadyExecuted)
            {
                return false; // Already executed, skip duplicate
            }
            return true; // First call, let it proceed
        }
    }
}
