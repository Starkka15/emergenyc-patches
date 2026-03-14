using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// CharacterManager patches — DIAGNOSTIC ONLY.
    /// Logs to CommunityFixes_diag.log via Plugin.Log() for guaranteed disk writes under Proton.
    /// </summary>
    [HarmonyPatch(typeof(CharacterManager))]
    public static class CharacterManagerPatches
    {
        [HarmonyPatch(nameof(CharacterManager.Spawn))]
        [HarmonyPrefix]
        public static void Spawn_PreLog(
            CharacterManager __instance,
            string firehouse, string engine, string role)
        {
            try
            {
                int spawnAreaCount = AcademyActorSpawn.spawnAreas?.Count ?? -1;
                int matchCount = -1;
                if (AcademyActorSpawn.spawnAreas != null)
                {
                    matchCount = AcademyActorSpawn.spawnAreas.FindAll(
                        p => p != null && p.loaderSetup.engine == engine && p.loaderSetup.role == role).Count;
                }
                float lastSpawned = Traverse.Create(__instance).Field("lastSpawned").GetValue<float>();
                string msg = $"[CharacterManager] Spawn CALLED: firehouse='{firehouse}' engine='{engine}' role='{role}'" +
                    $" firehouses={__instance.firehouses?.Count ?? -1}" +
                    $" skinning={CharacterManager.skinningSystemEnabled}" +
                    $" spawnAreas={spawnAreaCount} matching={matchCount}" +
                    $" lastSpawned={lastSpawned} time={Time.time}" +
                    $" cooldownOK={lastSpawned <= Time.time}";
                Plugin.Log(msg);
            }
            catch (Exception ex)
            {
                Plugin.Log($"[CharacterManager] Spawn_PreLog error: {ex}");
            }
        }

        [HarmonyPatch(nameof(CharacterManager.Spawn))]
        [HarmonyPostfix]
        public static void Spawn_Diagnostic(
            string firehouse, string engine, string role,
            GameObject __result)
        {
            if (__result == null)
                Plugin.Log($"[CharacterManager] Spawn returned NULL: engine='{engine}' role='{role}'");
            else
                Plugin.Log($"[CharacterManager] Spawn OK: {__result.name} engine='{engine}' role='{role}'");
        }

        [HarmonyPatch(nameof(CharacterManager.Spawn))]
        [HarmonyFinalizer]
        public static Exception? Spawn_CatchException(Exception __exception,
            string firehouse, string engine, string role)
        {
            if (__exception != null)
            {
                Plugin.Log($"[CharacterManager] Spawn THREW {__exception.GetType().Name}: {__exception.Message}" +
                    $" firehouse='{firehouse}' engine='{engine}' role='{role}'" +
                    $"\n{__exception.StackTrace}");
            }
            return __exception; // Don't suppress
        }

        [HarmonyPatch(nameof(CharacterManager.SpawnAI))]
        [HarmonyPostfix]
        public static void SpawnAI_Diagnostic(
            string engine, string role,
            GameObject __result)
        {
            if (__result == null)
                Plugin.Log($"[CharacterManager] SpawnAI returned null: engine='{engine}' role='{role}'");
            else
                Plugin.Log($"[CharacterManager] SpawnAI OK: {__result.name} engine='{engine}' role='{role}'");
        }
    }
}
