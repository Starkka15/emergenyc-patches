using System.Linq;
using HarmonyLib;
using UnityEngine;
using WoofTools.API;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for TrafficController despawn system:
    ///
    /// Bug 1: Distance measured from controller's static transform, NOT the camera.
    ///        Cars near the player but far from the controller vanish in front of you.
    ///        Fix: Use Camera.main position as the distance reference point.
    ///
    /// Bug 2: CanDeactivate() always returns true — no protection for visible cars.
    ///        Fix: Add a minimum camera distance floor before allowing deactivation.
    ///
    /// Bug 3: Over-target culling has no distance floor — kills nearest cars if over count.
    ///        Fix: Only cull cars beyond a minimum safe distance from the camera.
    ///
    /// Bug 4: Despawn range (45m) is far too low — cars pop out constantly.
    ///        Fix: Increase effective despawn range. Also increase spawn range.
    ///
    /// Bug 5: Hardcoded TrafficCongestion = Heavy on every tick (line 270).
    ///        Fix: Don't override if player has set a level.
    ///
    /// Bug 6: TrafficCarBaked.OnPathComplete dead-end leaves ghost car alive.
    ///        Fix: Destroy the car if no valid junction is found.
    /// </summary>
    [HarmonyPatch(typeof(TrafficController))]
    public static class TrafficControllerPatches
    {
        // Effective range overrides — much wider than the 30/45 defaults
        private const float EffectiveSpawnRange = 80f;
        private const float EffectiveDespawnRange = 180f;
        private const float MinSafeDistance = 40f; // Never despawn closer than this to camera

        // Replace the entire updateHandler method
        [HarmonyPatch("updateHandler")]
        [HarmonyPrefix]
        public static bool UpdateHandler_Fixed(TrafficController __instance)
        {
            if (__instance.trafficConfiguration == null)
                return false;

            // Bug 5 fix: Don't force Heavy if player set a level
            // Original code unconditionally sets Heavy every 0.5s
            if (__instance.level == 0)
            {
                __instance.trafficConfiguration.TrafficCongestion =
                    TrafficConfigurationModel.TrafficLevel.Heavy;
            }

            // Bug 1+2+4 fix: Use camera position and wider ranges
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 camPos = cam.transform.position;

            float despawnSq = EffectiveDespawnRange * EffectiveDespawnRange;
            float safeSq = MinSafeDistance * MinSafeDistance;

            // Despawn only cars that are far from the CAMERA, not the controller
            var toDespawn = __instance.VehiclePool.Items
                .Where(item =>
                {
                    if (item == null || !item.IsActive()) return false;
                    float distSq = (camPos - item.transform.position).sqrMagnitude;
                    // Only despawn if beyond effective range AND beyond safe distance
                    return distSq > despawnSq && distSq > safeSq;
                })
                .ToArray();

            for (int i = 0; i < toDespawn.Length; i++)
            {
                if (toDespawn[i] != null)
                    toDespawn[i].Deactivate();
            }

            // Traffic count management
            int targetCount;
            if (__instance.level > 0)
            {
                targetCount = __instance.level;
            }
            else if (__instance.level == 0)
            {
                // Original behavior for level 0: clear traffic
                __instance.VehiclePool.Items.ToList().ForEach(item =>
                {
                    if (item != null && item.IsActive()) item.Deactivate();
                });
                __instance.trafficConfiguration.ClearTraffic();
                __instance.trafficConfiguration.SetTrafficLevelNone();
                __instance.m_trafficCount = 0;
                return false;
            }
            else
            {
                targetCount = __instance.trafficConfiguration.GetCurrentTrafficLevel();
            }

            __instance.m_trafficCount = targetCount - __instance.VehiclePool.ActiveCount;

            // Spawn if under target
            var nextSpawnField = Traverse.Create(__instance).Field("nextSpawnInterval");
            if (__instance.m_trafficCount > 0 &&
                nextSpawnField.GetValue<float>() <= Time.realtimeSinceStartup)
            {
                __instance.VehiclePool.ActivateByPrefab(
                    __instance.trafficConfiguration.GetVehiclePrefab());
            }

            // Bug 3 fix: Over-target culling with safe distance floor
            if (__instance.m_trafficCount < 0)
            {
                // Only cull cars that are far enough from the camera
                var cullCandidate = __instance.VehiclePool.Items
                    .Where(item => item != null && item.IsActive() &&
                           (camPos - item.transform.position).sqrMagnitude > safeSq)
                    .OrderByDescending(item =>
                        (camPos - item.transform.position).sqrMagnitude)
                    .FirstOrDefault();

                if (cullCandidate != null)
                    cullCandidate.Deactivate();
            }

            return false; // Skip original
        }
    }

    /// <summary>
    /// Bug 4 fix: Override the spawn/despawn ranges on the TrafficConfigurationModel
    /// so that PositionVehicleOnNearbyRoad uses wider ranges too.
    /// </summary>
    [HarmonyPatch(typeof(TrafficConfigurationModel))]
    public static class TrafficConfigurationModelPatches
    {
        [HarmonyPatch("OnEnable")]
        [HarmonyPostfix]
        public static void OnEnable_WiderRanges(TrafficConfigurationModel __instance)
        {
            __instance.SpawnRange = 80f;
            __instance.DespawnRange = 180f;
            Plugin.Log($"[TrafficFix] Overriding spawn/despawn range: {__instance.SpawnRange}m / {__instance.DespawnRange}m");
        }
    }

    /// <summary>
    /// Bug 6 fix: TrafficCarBaked.OnPathComplete leaves ghost cars when no junction found.
    /// ReleaseCar() only clears reservations — the car stays alive doing nothing.
    /// Fix: Actually destroy/pool the car.
    /// </summary>
    [HarmonyPatch(typeof(TrafficCarBaked), "OnPathComplete")]
    public static class TrafficCarBakedOnPathCompletePatch
    {
        [HarmonyPrefix]
        public static bool OnPathComplete_FixGhost(TrafficCarBaked __instance)
        {
            var traverse = Traverse.Create(__instance);
            int lane = __instance.lane;
            int junction = __instance.juncation;

            // Store previous state for cleanup
            traverse.Field("lastLane").SetValue(lane);
            traverse.Field("lastJunctionv").SetValue(junction);
            traverse.Field("lastPointMatrixOwnedCache").SetValue(__instance.pointMatrixOwned);
            __instance.pointMatrixOwned = -1;
            __instance.indexStart = 0;

            if (junction != -1)
            {
                // Leaving a junction → go to next lane (normal path)
                __instance.lane = NodeDataContainer.instance.lanedata[lane]
                    .connectorPoints[junction].nextLane;
                __instance.juncation = -1;
                __instance.indexStart = 0;
                traverse.Field("cacheSequence").SetValue(
                    NodeDataContainer.instance.lanedata[__instance.lane].animationSequences);
            }
            else
            {
                // End of lane → pick next junction
                int nextJunction;
                if (!TrafficCarBaked.junctionReroute)
                {
                    nextJunction = NodeDataContainer.instance.lanedata[lane].GetRandomJunctionN();
                }
                else
                {
                    // Optimized reroute: pick least-congested junction
                    nextJunction = 0;
                    int bestCost = int.MaxValue;
                    int bestIdx = -1;
                    var connectors = NodeDataContainer.instance.lanedata[lane].connectorPoints;
                    for (int i = 0; i < connectors.Count; i++)
                    {
                        if (!connectors[i].isReserved)
                        {
                            int cost = connectors[i].aabbCost;
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                bestIdx = i;
                            }
                        }
                    }
                    if (bestIdx != -1)
                        nextJunction = bestIdx;
                }

                if (nextJunction == -1)
                {
                    // Bug 6 fix: No valid junction — clean up and destroy the car
                    // instead of leaving it as a ghost
                    __instance.ReleaseCar();
                    var poolable = __instance.GetComponent<IPoolable>();
                    if (poolable != null)
                    {
                        poolable.Deactivate();
                    }
                    else
                    {
                        Object.Destroy(__instance.gameObject);
                    }
                    return false;
                }

                __instance.juncation = nextJunction;
                __instance.indexStart = 0;
                traverse.Field("cacheSequence").SetValue(
                    NodeDataContainer.instance.lanedata[lane]
                        .connectorPoints[nextJunction].animationSequences);
            }

            // Notify callbacks
            foreach (var cb in __instance.trafficCallbacks)
            {
                cb.SetPath(__instance.lane, __instance.juncation, __instance.indexStart);
            }

            return false; // Skip original
        }
    }
}
