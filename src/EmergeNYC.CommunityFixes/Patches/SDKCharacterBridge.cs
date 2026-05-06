using HarmonyLib;
using UnityEngine;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.CommunityFixes.Patches
{
    // T13: Fire CharacterAPI events from CharacterManager and AIManager patch points.

    /// <summary>
    /// CharacterManager.SpawnAI — fire OnCharacterSpawned after the character is placed.
    /// SpawnAI returns void; the AIManager is on the spawned GameObject.
    /// We capture from CharacterManager's internal state post-spawn.
    /// </summary>
    [HarmonyPatch(typeof(CharacterManager), "SpawnAI",
        new[] { typeof(string), typeof(string), typeof(string), typeof(Vector3), typeof(Quaternion) })]
    public static class SDK_CharacterManager_SpawnAI
    {
        // SpawnAI doesn't return the AIManager directly; we hook AIManager.OnEnable as the spawn signal.
        [HarmonyPostfix]
        public static void Postfix() { /* spawn signal comes from SDK_AIManager_OnEnable */ }
    }

    [HarmonyPatch(typeof(AIManager), "OnEnable")]
    public static class SDK_AIManager_OnEnable
    {
        [HarmonyPostfix]
        public static void Postfix(AIManager __instance) =>
            CharacterAPI.RaiseCharacterSpawned(__instance);
    }

    /// <summary>
    /// Detect AI↔Player control transitions by watching AIControl/PlayerControl in Update.
    /// V14: Postfix reads current state; compare to previous stored state. No game method calls.
    /// </summary>
    [HarmonyPatch(typeof(AIManager), "Update")]
    public static class SDK_AIManager_ControlTransition
    {
        internal static readonly System.Collections.Generic.Dictionary<int, (bool ai, bool player)>
            _prev = new();

        [HarmonyPostfix]
        public static void Postfix(AIManager __instance)
        {
            int id = __instance.GetInstanceID();
            bool curAI = __instance.AIControl;
            bool curPlayer = __instance.PlayerControl;

            if (!_prev.TryGetValue(id, out var prev))
            {
                _prev[id] = (curAI, curPlayer);
                return;
            }

            if (curAI && !prev.ai)
                CharacterAPI.RaiseAITakeControl(__instance);
            else if (curPlayer && !prev.player)
                CharacterAPI.RaisePlayerTakeControl(__instance);

            _prev[id] = (curAI, curPlayer);
        }
    }

    [HarmonyPatch(typeof(AIManager), "OnDisable")]
    public static class SDK_AIManager_OnDisable
    {
        [HarmonyPostfix]
        public static void Postfix(AIManager __instance) =>
            SDK_AIManager_ControlTransition._prev.Remove(__instance.GetInstanceID());
    }
}
