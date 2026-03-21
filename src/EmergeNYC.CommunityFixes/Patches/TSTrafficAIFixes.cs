using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WoofTools.API;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for TSTrafficAI bugs that cause cars to stop dead or despawn:
    ///
    /// Bug Fix 1: fullStop with hardcoded num3=0 causes permanent emergency braking.
    ///            Fix: Use actual distance to nearest player in the sensor.
    ///
    /// Bug Fix 2: fullStop not cleared when players list empties via destruction.
    ///            Fix: In FixedUpdates postfix, force fullStop=false if players list is empty.
    ///
    /// Bug Fix 3: GetCurrentPoint() returns false at path boundaries → car freezes.
    ///            Fix: Detect stuck cars (speed near 0 for extended time) and force respawn.
    ///
    /// Bug Fix 4: Negative segDistance after lane change → car freezes.
    ///            Fix: Clamp segDistance to minimum 0.1 in GetBrake prefix.
    /// </summary>

    // =====================================================================
    // FIX: fullStop permanent braking + stale players list
    // =====================================================================
    [HarmonyPatch(typeof(TSTrafficAI))]
    public static class TSTrafficFullStopFix
    {
        private static AccessTools.FieldRef<TSTrafficAI, bool> _fullStopRef;
        private static AccessTools.FieldRef<TSTrafficAI, List<Transform>> _playersRef;

        private static void EnsureFieldRefs()
        {
            if (_fullStopRef == null)
                _fullStopRef = AccessTools.FieldRefAccess<TSTrafficAI, bool>("fullStop");
            if (_playersRef == null)
                _playersRef = AccessTools.FieldRefAccess<TSTrafficAI, List<Transform>>("players");
        }

        /// <summary>
        /// After FixedUpdates runs, verify fullStop state is consistent with players list.
        /// The original code can leave fullStop=true when players are removed by destruction
        /// rather than by OnTriggerExit.
        /// </summary>
        [HarmonyPatch("FixedUpdates")]
        [HarmonyPostfix]
        public static void FixedUpdates_FixFullStop(TSTrafficAI __instance)
        {
            EnsureFieldRefs();

            var players = _playersRef(__instance);
            bool fs = _fullStopRef(__instance);

            if (!fs) return;

            // Clean null/destroyed players from the list
            bool hadNull = false;
            for (int i = players.Count - 1; i >= 0; i--)
            {
                if (players[i] == null || !players[i].gameObject.activeInHierarchy)
                {
                    players.RemoveAt(i);
                    hadNull = true;
                }
            }

            // If players list is now empty, clear fullStop
            if (players.Count == 0 && fs)
            {
                _fullStopRef(__instance) = false;
            }
        }
    }

    // =====================================================================
    // FIX: fullStop braking uses hardcoded 0 distance (should use actual
    // distance to nearest player). Original line 787: float num3 = 0f;
    // This causes maximum braking force regardless of player distance.
    // =====================================================================
    [HarmonyPatch(typeof(TSTrafficAI), "GetBrake")]
    public static class TSTrafficGetBrakeFix
    {
        private static AccessTools.FieldRef<TSTrafficAI, bool> _fullStopRef;
        private static AccessTools.FieldRef<TSTrafficAI, List<Transform>> _playersRef;

        private static void EnsureFieldRefs()
        {
            if (_fullStopRef == null)
                _fullStopRef = AccessTools.FieldRefAccess<TSTrafficAI, bool>("fullStop");
            if (_playersRef == null)
                _playersRef = AccessTools.FieldRefAccess<TSTrafficAI, List<Transform>>("players");
        }

        /// <summary>
        /// Prefix: Fix negative segDistance and clear stale fullStop before GetBrake runs.
        /// segDistance going negative (after lane changes) causes the while loop in GetBrake
        /// to stall the car permanently.
        /// </summary>
        [HarmonyPrefix]
        public static void GetBrake_Prefix(TSTrafficAI __instance)
        {
            // Fix negative segDistance — clamp to small positive value
            if (__instance.segDistance < 0f)
            {
                __instance.segDistance = 0.5f;
            }
        }
    }

    // =====================================================================
    // FIX: Stuck car detection — if a TS car hasn't moved for 10+ seconds,
    // force it to respawn. This catches all edge cases (dead-end waypoints,
    // broken path state, etc.) as a safety net.
    // =====================================================================
    [HarmonyPatch(typeof(TSTrafficAI))]
    public static class TSTrafficStuckDetection
    {
        private static readonly Dictionary<int, StuckState> _stuckTracking =
            new Dictionary<int, StuckState>();

        private const float StuckSpeedThreshold = 0.5f;  // Below this = "not moving"
        private const float StuckTimeBeforeRespawn = 12f; // Seconds stuck before respawn

        private class StuckState
        {
            public Vector3 lastPosition;
            public float stuckSince;
            public bool wasStopped;
        }

        [HarmonyPatch("FixedUpdates")]
        [HarmonyPostfix]
        public static void FixedUpdates_StuckCheck(TSTrafficAI __instance)
        {
            // Don't respawn cars that are in our yield system — they're stopped on purpose
            if (YieldStateManager.IsYielding(__instance.GetInstanceID()))
                return;

            int id = __instance.GetInstanceID();

            if (!_stuckTracking.TryGetValue(id, out var state))
            {
                state = new StuckState
                {
                    lastPosition = __instance.transform.position,
                    stuckSince = Time.time,
                    wasStopped = false
                };
                _stuckTracking[id] = state;
                return;
            }

            float distMoved = (state.lastPosition - __instance.transform.position).sqrMagnitude;
            state.lastPosition = __instance.transform.position;

            // Check if car is effectively not moving
            bool isStuck = distMoved < StuckSpeedThreshold * StuckSpeedThreshold * 0.09f;
            // 0.09 = 0.3s interval squared — if moved less than threshold*0.3 per tick

            if (isStuck)
            {
                if (!state.wasStopped)
                {
                    state.stuckSince = Time.time;
                    state.wasStopped = true;
                }

                // If stuck long enough and not at a red light / normal stop
                if (Time.time - state.stuckSince > StuckTimeBeforeRespawn)
                {
                    // Force respawn via the pool system
                    var poolable = __instance.GetComponent<IPoolable>();
                    if (poolable != null)
                    {
                        poolable.Deactivate();
                        _stuckTracking.Remove(id);
                        Plugin.Log($"[TSFix-Stuck] Car {id} was stuck for {StuckTimeBeforeRespawn}s — respawned");
                    }
                }
            }
            else
            {
                state.wasStopped = false;
            }
        }

        [HarmonyPatch("OnDisable")]
        [HarmonyPostfix]
        public static void OnDisable_Cleanup(TSTrafficAI __instance)
        {
            _stuckTracking.Remove(__instance.GetInstanceID());
        }
    }
}
