using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Traffic pullover for emergency vehicles (v8).
    ///
    /// Two traffic systems:
    ///   1. TrafficCarBaked (baked animation paths) — handled HERE with offset system
    ///   2. TSTrafficAI (real-time AI steering) — handled in EmergencyVehicleYieldPatches.cs
    ///
    /// This file also contains:
    ///   - TSTrafficCanMoveFix: patches canMove setter to prevent Object.Destroy
    ///   - CastEmergencySoundsPatch: disables original emergency sound system
    /// </summary>

    // =====================================================================
    // BAKED TRAFFIC PULLOVER (TrafficCarBaked — path-based offset system)
    // =====================================================================
    [HarmonyPatch(typeof(TrafficCarBaked))]
    public static class TrafficPulloverPatches
    {
        internal static readonly Dictionary<int, PulloverState> _activePullovers =
            new Dictionary<int, PulloverState>();

        private const float PulloverOffset = 3.5f;
        private const float PulloverSpeed = 2.5f;
        private const float ReturnSpeed = 1.5f;
        private const float SirenCheckRadius = 60f;
        private const float HoldTimeAfterClear = 3f;
        private const float PulloverCooldown = 2f;
        private const float SeekTimeout = 8f;
        private const float ObstacleCheckRadius = 1.0f;
        private const float ObstacleCheckDist = 4.5f;

        private static AccessTools.FieldRef<TrafficCarBaked, float> _speedRef;
        private static AccessTools.FieldRef<TrafficCarBaked, float> _threadLastDelayRef;

        internal static readonly Dictionary<int, float> _pulloverCooldowns =
            new Dictionary<int, float>();

        internal enum Phase
        {
            Seeking,
            Sliding,
            Waiting,
            Merging
        }

        internal class PulloverState
        {
            public float currentOffset;
            public float targetOffset;
            public float sirenClearTime;
            public Vector3 offsetDirection;
            public Vector3 lastAppliedOffset;
            public Phase phase;
        }

        private static void EnsureFieldRefs()
        {
            if (_speedRef == null)
                _speedRef = AccessTools.FieldRefAccess<TrafficCarBaked, float>("currentSpeed");
            if (_threadLastDelayRef == null)
                _threadLastDelayRef = AccessTools.FieldRefAccess<TrafficCarBaked, float>("threadLastDelay");
        }

        public static void StartPullover(TrafficCarBaked instance)
        {
            int id = instance.GetInstanceID();

            if (_activePullovers.TryGetValue(id, out var existing))
            {
                existing.sirenClearTime = Time.time;
                return;
            }

            if (_pulloverCooldowns.TryGetValue(id, out float cooldownEnd) && Time.time < cooldownEnd)
                return;

            EnsureFieldRefs();

            var state = new PulloverState
            {
                currentOffset = 0f,
                targetOffset = PulloverOffset,
                sirenClearTime = Time.time,
                offsetDirection = instance.transform.right.normalized,
                lastAppliedOffset = Vector3.zero,
                phase = Phase.Seeking
            };

            _activePullovers[id] = state;
            instance.StartCoroutine(PulloverCoroutine(instance, state));

            Plugin.Log($"[Pullover] Car {id} seeking clear spot (lane={instance.lane})");
        }

        [HarmonyPatch(nameof(TrafficCarBaked.FreeLane))]
        [HarmonyPrefix]
        public static bool FreeLane_PulloverOffset(TrafficCarBaked __instance)
        {
            StartPullover(__instance);
            return false;
        }

        [HarmonyPatch("SwitchToLane")]
        [HarmonyPrefix]
        public static bool SwitchToLane_Block(TrafficCarBaked __instance, ref IEnumerator __result)
        {
            if (_activePullovers.ContainsKey(__instance.GetInstanceID()))
            {
                __result = EmptyCoroutine();
                return false;
            }
            return true;
        }

        private static IEnumerator EmptyCoroutine()
        {
            yield break;
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static void Update_RemoveOffset(TrafficCarBaked __instance)
        {
            int id = __instance.GetInstanceID();
            if (!_activePullovers.TryGetValue(id, out var state))
                return;

            EnsureFieldRefs();

            if (state.lastAppliedOffset.sqrMagnitude > 0.0001f)
                __instance.transform.position -= state.lastAppliedOffset;

            if (state.phase == Phase.Sliding || state.phase == Phase.Waiting)
                _threadLastDelayRef(__instance) = Time.time + 0.5f;

            if (state.phase == Phase.Waiting)
                _speedRef(__instance) = 0f;
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_ApplyOffset(TrafficCarBaked __instance)
        {
            int id = __instance.GetInstanceID();
            if (!_activePullovers.TryGetValue(id, out var state))
                return;

            if (state.currentOffset <= 0.001f)
            {
                state.lastAppliedOffset = Vector3.zero;
                return;
            }

            state.offsetDirection = __instance.transform.right.normalized;
            state.lastAppliedOffset = state.offsetDirection * state.currentOffset;
            __instance.transform.position += state.lastAppliedOffset;
        }

        private static bool IsRightSideClear(TrafficCarBaked instance)
        {
            Vector3 origin = instance.transform.position + Vector3.up * 0.5f;
            Vector3 right = instance.transform.right.normalized;
            return !Physics.SphereCast(origin, ObstacleCheckRadius, right,
                out _, ObstacleCheckDist, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }

        private static IEnumerator PulloverCoroutine(TrafficCarBaked instance, PulloverState state)
        {
            int id = instance.GetInstanceID();

            // Phase 0: Seek a clear gap
            state.phase = Phase.Seeking;
            float seekStart = Time.time;
            bool foundClearSpot = false;

            while (Time.time - seekStart < SeekTimeout)
            {
                if (instance == null || !instance.gameObject.activeInHierarchy)
                {
                    _activePullovers.Remove(id);
                    yield break;
                }

                if (IsRightSideClear(instance))
                {
                    foundClearSpot = true;
                    Plugin.Log($"[Pullover] Car {id} found clear spot, sliding right");
                    break;
                }

                yield return null;
            }

            if (!foundClearSpot)
            {
                Plugin.Log($"[Pullover] Car {id} no clear spot, braking on rail");
                state.phase = Phase.Waiting;

                while (true)
                {
                    if (instance == null || !instance.gameObject.activeInHierarchy)
                    {
                        _activePullovers.Remove(id);
                        yield break;
                    }

                    EnsureFieldRefs();
                    _speedRef(instance) = 0f;

                    bool sirenNearby = EmergencyVehicleRegistry.GetNearestSiren(
                        instance.transform.position, SirenCheckRadius) != null;
                    if (sirenNearby)
                        state.sirenClearTime = Time.time;

                    if (!sirenNearby && Time.time - state.sirenClearTime > HoldTimeAfterClear)
                        break;

                    yield return null;
                }

                _activePullovers.Remove(id);
                _pulloverCooldowns[id] = Time.time + PulloverCooldown;
                Plugin.Log($"[Pullover] Car {id} resuming (was braked on rail)");
                yield break;
            }

            // Phase 1: Slide right
            state.phase = Phase.Sliding;

            while (state.currentOffset < state.targetOffset - 0.05f)
            {
                if (instance == null || !instance.gameObject.activeInHierarchy)
                {
                    _activePullovers.Remove(id);
                    yield break;
                }

                if (state.currentOffset > 0.5f && !IsRightSideClear(instance))
                {
                    Plugin.Log($"[Pullover] Car {id} obstacle mid-slide at {state.currentOffset:F1}m");
                    break;
                }

                state.currentOffset = Mathf.MoveTowards(
                    state.currentOffset, state.targetOffset, PulloverSpeed * Time.deltaTime);

                yield return null;
            }

            if (state.currentOffset >= state.targetOffset - 0.05f)
                state.currentOffset = state.targetOffset;

            // Phase 2: Wait on shoulder
            state.phase = Phase.Waiting;
            Plugin.Log($"[Pullover] Car {id} stopped at offset={state.currentOffset:F1}m");

            while (true)
            {
                if (instance == null || !instance.gameObject.activeInHierarchy)
                {
                    _activePullovers.Remove(id);
                    yield break;
                }

                bool sirenNearby = EmergencyVehicleRegistry.GetNearestSiren(
                    instance.transform.position, SirenCheckRadius) != null;
                if (sirenNearby)
                    state.sirenClearTime = Time.time;

                if (!sirenNearby && Time.time - state.sirenClearTime > HoldTimeAfterClear)
                    break;

                yield return null;
            }

            // Phase 3: Merge back
            state.phase = Phase.Merging;
            state.targetOffset = 0f;
            Plugin.Log($"[Pullover] Car {id} merging back");

            while (state.currentOffset > 0.05f)
            {
                if (instance == null || !instance.gameObject.activeInHierarchy)
                {
                    _activePullovers.Remove(id);
                    yield break;
                }

                state.currentOffset = Mathf.MoveTowards(
                    state.currentOffset, 0f, ReturnSpeed * Time.deltaTime);

                yield return null;
            }

            state.currentOffset = 0f;
            state.lastAppliedOffset = Vector3.zero;
            _activePullovers.Remove(id);
            _pulloverCooldowns[id] = Time.time + PulloverCooldown;
            Plugin.Log($"[Pullover] Car {id} back on rail");
        }

        [HarmonyPatch("OnDisable")]
        [HarmonyPostfix]
        public static void OnDisable_Cleanup(TrafficCarBaked __instance)
        {
            int id = __instance.GetInstanceID();
            _activePullovers.Remove(id);
            _pulloverCooldowns.Remove(id);
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        public static void OnDestroy_Cleanup(TrafficCarBaked __instance)
        {
            int id = __instance.GetInstanceID();
            _activePullovers.Remove(id);
            _pulloverCooldowns.Remove(id);
        }
    }

    // =====================================================================
    // FIX canMove SETTER — the original calls Object.Destroy(gameObject, 20f)
    // on EVERY set (even canMove = true!). This silently despawns traffic.
    // We replace the setter to just update lastSet without scheduling Destroy.
    // =====================================================================
    [HarmonyPatch(typeof(TSTrafficAI))]
    public static class TSTrafficCanMoveFix
    {
        private static AccessTools.FieldRef<TSTrafficAI, float> _lastSetRef;

        private static void EnsureFieldRef()
        {
            if (_lastSetRef == null)
                _lastSetRef = AccessTools.FieldRefAccess<TSTrafficAI, float>("lastSet");
        }

        [HarmonyPatch(nameof(TSTrafficAI.canMove), MethodType.Setter)]
        [HarmonyPrefix]
        public static bool CanMove_SetterFix(TSTrafficAI __instance, bool value)
        {
            EnsureFieldRef();
            if (!value)
            {
                _lastSetRef(__instance) = Time.time;
            }
            // Skip original — prevents Object.Destroy(gameObject, 20f)
            return false;
        }
    }

    /// <summary>
    /// Disable CastEmergencySounds entirely. The original does two harmful things:
    ///   1. Calls BlockTraffic() which sets canMove=false (stops cars, triggers despawn)
    ///   2. Calls GetOutOfLane() which makes cars hard-turn into parked cars
    /// Our SirenTrafficBroadcastPatch + TSTrafficPulloverPatch handle traffic properly.
    /// CastEmergencySounds is also called by air horn — we don't want the horn to
    /// stop traffic, only to flush already-stopped cars via AirhornFlushPatch.
    /// </summary>
    [HarmonyPatch(typeof(RCC_CarControllerV3), "CastEmergencySounds")]
    public static class CastEmergencySoundsPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            // Skip entirely — traffic control handled by our pullover system
            return false;
        }
    }
}
