using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for TrafficSpawner:
    /// 1. Update() allocates a new List every frame when canDespawn is true (line 83).
    ///    This creates GC pressure in a hot path. Fix: use a static reusable list.
    /// 2. Camera.main accessed per-iteration in the despawn loop - cache it once.
    /// </summary>
    [HarmonyPatch(typeof(TrafficSpawner))]
    public static class TrafficSpawnerPatches
    {
        private static readonly List<GameObject> _despawnBuffer = new List<GameObject>();

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool Update_ReduceAllocations(TrafficSpawner __instance)
        {
            var enumeratorComplete = Traverse.Create(__instance).Field("enumeratorComplete").GetValue<bool>();
            if (!enumeratorComplete) return false;

            var nextSpawn = Traverse.Create(__instance).Field("nextSpawn");

            if (__instance.canSpawn && nextSpawn.GetValue<float>() < Time.time &&
                __instance.activeCars.Count < (int)__instance.nodeConfig.trafficConfig.trafficCongestion)
            {
                int pointMatrix = -1;
                int spawnLocation = TrafficMatrixUpdater.instance.GetSpawnLocation(
                    __instance.maxOcupancy, __instance.headroom, out pointMatrix);

                if (spawnLocation != -1 && pointMatrix > 0)
                {
                    GameObject nextCar = __instance.nodeConfig.trafficConfig.GetNextCar();
                    nextCar.GetComponent<TrafficCarBaked>().SetPath(spawnLocation, -1, pointMatrix);
                    __instance.activeCars.Add(nextCar);
                    nextSpawn.SetValue(__instance.minIntervalForSpawn + Time.time);
                }
            }

            if (!__instance.canDespawn) return false;

            // Fix 1: Reuse static list instead of allocating every frame
            _despawnBuffer.Clear();

            // Fix 2: Cache Camera.main once
            var cam = Camera.main;
            if (cam == null)
            {
                __instance.activeCars.RemoveAll(p => p == null);
                return false;
            }

            var camPos = cam.transform.position;
            float despawnRange = __instance.nodeConfig.spawnDespawnRange.y;

            for (int i = 0; i < __instance.activeCars.Count; i++)
            {
                if (__instance.activeCars[i] != null &&
                    Vector3.Distance(camPos, __instance.activeCars[i].transform.position) > despawnRange)
                {
                    _despawnBuffer.Add(__instance.activeCars[i]);
                }
            }

            __instance.activeCars.RemoveAll(p => p == null);

            for (int i = 0; i < _despawnBuffer.Count; i++)
            {
                __instance.activeCars.Remove(_despawnBuffer[i]);
                SimplePool.Despawn(_despawnBuffer[i]);
            }

            return false; // Skip original
        }
    }
}
