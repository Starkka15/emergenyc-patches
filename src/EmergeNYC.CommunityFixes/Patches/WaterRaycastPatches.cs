using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for WaterRaycast:
    /// 1. CRITICAL MEMORY LEAK: Instantiates a new Puddle GameObject every FixedUpdate (50x/sec)
    ///    while spraying water, and NEVER destroys them. After 1 minute of spraying = 3000 objects.
    ///    After 10 minutes = 30,000 objects. This is likely a major cause of performance
    ///    degradation during extended fire-fighting sessions.
    ///    Fix: Pool puddles and limit max count, auto-destroy old ones.
    /// </summary>
    [HarmonyPatch(typeof(WaterRaycast))]
    public static class WaterRaycastPatches
    {
        private static readonly Dictionary<int, Queue<GameObject>> _puddlePools = new();
        private static readonly Dictionary<int, float> _lastSpawnTime = new();
        private const int MaxPuddles = 30;
        private const float SpawnInterval = 0.5f; // Only spawn a puddle every 0.5s instead of every FixedUpdate

        [HarmonyPatch("FixedUpdate")]
        [HarmonyPrefix]
        public static bool FixedUpdate_FixPuddleLeak(WaterRaycast __instance)
        {
            if (!__instance.isSpraying)
                return false;

            if (!Physics.Raycast(__instance.transform.position, __instance.transform.forward,
                out RaycastHit hitInfo, 50f, __instance.layermask))
                return false;

            Debug.DrawRay(__instance.transform.position, __instance.transform.forward, Color.green);

            // Throttle puddle spawning
            int id = __instance.GetInstanceID();
            if (_lastSpawnTime.TryGetValue(id, out float lastTime) &&
                Time.time - lastTime < SpawnInterval)
                return false;

            _lastSpawnTime[id] = Time.time;

            if (__instance.Puddle == null)
                return false;

            // Get or create pool for this instance
            if (!_puddlePools.TryGetValue(id, out var pool))
            {
                pool = new Queue<GameObject>();
                _puddlePools[id] = pool;
            }

            // Recycle old puddles when over limit
            while (pool.Count >= MaxPuddles)
            {
                var old = pool.Dequeue();
                if (old != null)
                    Object.Destroy(old);
            }

            var puddle = Object.Instantiate(__instance.Puddle, hitInfo.point,
                Quaternion.LookRotation(hitInfo.normal));
            pool.Enqueue(puddle);

            // Auto-destroy after 30 seconds as a safety net
            Object.Destroy(puddle, 30f);

            return false; // Skip original
        }
    }
}
