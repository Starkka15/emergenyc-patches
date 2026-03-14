using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Traffic pullover for emergency vehicles (v7).
    ///
    /// Two traffic systems exist:
    ///   1. TrafficCarBaked (baked animation paths) — managed by TrafficSpawner/TrafficMatrixUpdater
    ///   2. TSTrafficAI (real-time AI steering) — managed by TrafficController
    ///
    /// v7 changes (TS traffic):
    ///   - canMove=false triggered Object.Destroy(gameObject,20f) — cars were being destroyed!
    ///   - Now uses myLaneOffset to naturally steer TS cars right (toward curb)
    ///   - Uses lastSet field directly to stop cars without triggering Destroy
    ///   - Cars steer right → stop on shoulder → wait → merge back when sirens clear
    ///   - Cars never cross center line (positive offset = right in local space)
    ///   - If right side blocked (parked cars etc), car stops in current lane
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

                    bool sirenNearby = SirenTrafficBroadcastPatch.IsAnySirenNearby(
                        instance.transform.position, SirenCheckRadius);
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

                bool sirenNearby = SirenTrafficBroadcastPatch.IsAnySirenNearby(
                    instance.transform.position, SirenCheckRadius);
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
    // TS TRAFFIC PULLOVER (TSTrafficAI — physics-based steering system)
    //
    // Uses myLaneOffset to naturally steer cars right via the AI's own
    // steering system. Positive myLaneOffset = right in car's local space.
    // Uses lastSet field directly to brake (avoids canMove setter which
    // calls Object.Destroy(gameObject, 20f) — a game bug).
    // =====================================================================
    [HarmonyPatch(typeof(TSTrafficAI))]
    public static class TSTrafficPulloverPatch
    {
        internal static readonly Dictionary<int, TSPulloverState> _activePullovers =
            new Dictionary<int, TSPulloverState>();

        // Cooldown after airhorn flush — car must drive forward before re-pulling over
        internal static readonly Dictionary<int, float> _airhornCooldowns =
            new Dictionary<int, float>();

        private static AccessTools.FieldRef<TSTrafficAI, float> _myLaneOffsetRef;
        private static AccessTools.FieldRef<TSTrafficAI, float> _lastSetRef;

        // How far right to steer (in local-space units) — conservative to avoid parked cars
        private const float PulloverExtraOffset = 1.8f;
        // How fast offset changes per second (applied every ~0.3s tick)
        private const float OffsetRate = 1.2f;
        // How fast offset restores when resuming (slower for smooth merge)
        private const float ResumeOffsetRate = 0.7f;
        private const float SirenCheckRadius = 60f;
        private const float HoldTimeAfterClear = 4f;
        private const float SeekTimeout = 6f;
        // Obstacle detection for right-side check
        private const float ObstacleSphereRadius = 1.5f;
        private const float ObstacleCastDist = 5f;

        public enum TSPhase
        {
            SteeringRight,   // Car still moving, steering toward curb
            Straightening,   // Offset reached, car driving forward to straighten out
            Stopped,         // Pulled over, fully braked
            Resuming         // Merging back into lane
        }

        public class TSPulloverState
        {
            public float originalOffset;
            public float currentExtraOffset;
            public float sirenClearTime;
            public float phaseStartTime;
            public TSPhase phase;
        }

        private static void EnsureFieldRefs()
        {
            if (_myLaneOffsetRef == null)
                _myLaneOffsetRef = AccessTools.FieldRefAccess<TSTrafficAI, float>("myLaneOffset");
            if (_lastSetRef == null)
                _lastSetRef = AccessTools.FieldRefAccess<TSTrafficAI, float>("lastSet");
        }

        /// <summary>
        /// Begin or refresh a TS car pullover. Called from SirenTrafficBroadcastPatch
        /// for each TS car within siren range.
        /// </summary>
        public static void StartTSPullover(TSTrafficAI instance)
        {
            int id = instance.GetInstanceID();

            if (_activePullovers.TryGetValue(id, out var existing))
            {
                // Already pulling over — refresh siren timestamp
                existing.sirenClearTime = Time.time;
                // If car was resuming, pull it back to stopped
                if (existing.phase == TSPhase.Resuming)
                {
                    existing.phase = TSPhase.Stopped;
                    existing.phaseStartTime = Time.time;
                }
                return;
            }

            // Airhorn cooldown — car was flushed, give it time to drive forward
            if (_airhornCooldowns.TryGetValue(id, out float cooldownEnd) && Time.time < cooldownEnd)
                return;

            EnsureFieldRefs();

            var state = new TSPulloverState
            {
                originalOffset = _myLaneOffsetRef(instance),
                currentExtraOffset = 0f,
                sirenClearTime = Time.time,
                phaseStartTime = Time.time,
                phase = TSPhase.SteeringRight
            };

            _activePullovers[id] = state;
            Plugin.Log($"[TS-Pullover] Car {id} starting pullover (origOffset={state.originalOffset:F2})");
        }

        /// <summary>
        /// Prefix on controllerAI — runs every ~0.3s per TS car (InvokeRepeating).
        /// Modifies myLaneOffset before steering is calculated, and uses lastSet
        /// to brake without triggering the canMove setter's Destroy call.
        /// </summary>
        [HarmonyPatch("controllerAI")]
        [HarmonyPrefix]
        public static void ControllerAI_Prefix(TSTrafficAI __instance)
        {
            int id = __instance.GetInstanceID();
            if (!_activePullovers.TryGetValue(id, out var state))
                return;

            EnsureFieldRefs();

            // controllerAI fires every ~0.3s via InvokeRepeating
            float dt = 0.3f;

            switch (state.phase)
            {
                case TSPhase.SteeringRight:
                {
                    // Measure how far right we can go (graduated, not binary)
                    float clearance = GetRightClearance(__instance);
                    // Use whatever room is available, even small amounts
                    float maxOffset = Mathf.Min(PulloverExtraOffset, clearance);

                    if (maxOffset > 0.05f && state.currentExtraOffset < maxOffset)
                    {
                        // Gradually increase offset — car's steering naturally follows
                        state.currentExtraOffset = Mathf.MoveTowards(
                            state.currentExtraOffset, maxOffset,
                            OffsetRate * dt);
                    }

                    // Apply the combined offset (original + pullover extra)
                    _myLaneOffsetRef(__instance) = state.originalOffset + state.currentExtraOffset;

                    // Transition to Straightening when we've used available space or timed out
                    bool reachedMax = state.currentExtraOffset >= maxOffset - 0.05f;
                    bool timedOut = Time.time - state.phaseStartTime > SeekTimeout;

                    if (reachedMax || timedOut)
                    {
                        state.phase = TSPhase.Straightening;
                        state.phaseStartTime = Time.time;
                        // Keep driving (canMove stays true) so car straightens along path
                        Plugin.Log($"[TS-Pullover] Car {id} straightening (offset={state.currentExtraOffset:F2})");
                    }
                    break;
                }

                case TSPhase.Straightening:
                {
                    // Keep offset constant — car drives forward and naturally aligns with road
                    _myLaneOffsetRef(__instance) = state.originalOffset + state.currentExtraOffset;

                    // After 2 seconds of driving straight, stop the car
                    if (Time.time - state.phaseStartTime > 2f)
                    {
                        state.phase = TSPhase.Stopped;
                        state.phaseStartTime = Time.time;
                        _lastSetRef(__instance) = Time.time;
                        Plugin.Log($"[TS-Pullover] Car {id} stopped (offset={state.currentExtraOffset:F2})");
                    }
                    break;
                }

                case TSPhase.Stopped:
                {
                    // Keep car braked by refreshing lastSet (canMove stays false)
                    _lastSetRef(__instance) = Time.time;
                    // Maintain the offset
                    _myLaneOffsetRef(__instance) = state.originalOffset + state.currentExtraOffset;

                    // Siren proximity is refreshed by the broadcast calling StartTSPullover
                    // (no FindObjectsOfType here — that was killing FPS)
                    // Also force-resume after 30s as a safety net
                    bool sirenTimeout = Time.time - state.sirenClearTime > HoldTimeAfterClear;
                    bool safetyTimeout = Time.time - state.phaseStartTime > 30f;

                    if (sirenTimeout || safetyTimeout)
                    {
                        state.phase = TSPhase.Resuming;
                        state.phaseStartTime = Time.time;
                        // Set lastSet far in the past so canMove immediately returns true
                        _lastSetRef(__instance) = Time.time - 3f;
                        Plugin.Log($"[TS-Pullover] Car {id} resuming (merging back){(safetyTimeout ? " [safety]" : "")}");
                    }
                    break;
                }

                case TSPhase.Resuming:
                {
                    // Force canMove true every tick — prevent anything from re-stopping
                    _lastSetRef(__instance) = Time.time - 3f;

                    // Gradually restore offset — car merges back into lane naturally
                    state.currentExtraOffset = Mathf.MoveTowards(
                        state.currentExtraOffset, 0f, ResumeOffsetRate * dt);

                    _myLaneOffsetRef(__instance) = state.originalOffset + state.currentExtraOffset;

                    if (state.currentExtraOffset <= 0.05f)
                    {
                        // Fully restored — remove from tracking
                        _myLaneOffsetRef(__instance) = state.originalOffset;
                        _activePullovers.Remove(id);
                        Plugin.Log($"[TS-Pullover] Car {id} back in lane");
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Measure how far right the car can offset before hitting an obstacle.
        /// Returns available clearance in meters (0 = no room, up to PulloverExtraOffset).
        /// Starts from outside the car's own body to avoid self-collision.
        /// </summary>
        private static float GetRightClearance(TSTrafficAI instance)
        {
            Vector3 right = instance.transform.right;
            // Start 1.5m to the right of center (just outside car body) and 1m up
            Vector3 origin = instance.transform.position + Vector3.up * 1.0f + right * 1.5f;
            // Use wider sphere (1.0m radius) to catch parked cars reliably
            if (Physics.SphereCast(origin, 1.0f, right, out RaycastHit hit,
                PulloverExtraOffset + 1f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                // Subtract 1.0m margin for car's half-width
                return Mathf.Max(0f, hit.distance - 1.0f);
            }
            // Nothing hit — full clearance available
            return PulloverExtraOffset;
        }

        /// <summary>
        /// Public access to EnsureFieldRefs for use by AirhornFlushPatch.
        /// </summary>
        public static void EnsureFieldRefsPublic()
        {
            EnsureFieldRefs();
        }

        /// <summary>
        /// Restore a car's lane offset and canMove state. Used by airhorn flush.
        /// </summary>
        public static void RestoreCarState(TSTrafficAI instance, TSPulloverState state)
        {
            EnsureFieldRefs();
            _myLaneOffsetRef(instance) = state.originalOffset;
            _lastSetRef(instance) = Time.time - 3f; // canMove = true immediately
        }

        [HarmonyPatch("OnDisable")]
        [HarmonyPostfix]
        public static void OnDisable_Cleanup(TSTrafficAI __instance)
        {
            int id = __instance.GetInstanceID();
            if (_activePullovers.TryGetValue(id, out var state))
            {
                // Restore original offset before removal
                EnsureFieldRefs();
                _myLaneOffsetRef(__instance) = state.originalOffset;
                _activePullovers.Remove(id);
            }
            _airhornCooldowns.Remove(id);
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

    // =====================================================================
    // AIR HORN — flush stopped TS cars so they drive forward and re-attempt
    // pullover. Hooked on FFD_Airhorn.OnPlay (fires when horn key pressed).
    // =====================================================================
    [HarmonyPatch(typeof(FFD_Airhorn), "OnPlay")]
    public static class AirhornFlushPatch
    {
        private const float FlushRadius = 40f;

        [HarmonyPostfix]
        public static void OnPlay_FlushTraffic(FFD_Airhorn __instance)
        {
            if (__instance.car == null) return;
            Vector3 hornPos = __instance.car.transform.position;
            float radiusSq = FlushRadius * FlushRadius;
            int flushed = 0;

            // Find all stopped pullover cars within range and force them to resume driving
            var toFlush = new List<int>();
            foreach (var kvp in TSTrafficPulloverPatch._activePullovers)
            {
                if (kvp.Value.phase == TSTrafficPulloverPatch.TSPhase.Stopped)
                    toFlush.Add(kvp.Key);
            }

            if (TSTrafficAI.trafficAIList == null) return;

            for (int i = 0; i < TSTrafficAI.trafficAIList.Count; i++)
            {
                var tsai = TSTrafficAI.trafficAIList[i];
                if (tsai == null) continue;
                int id = tsai.GetInstanceID();
                if (!toFlush.Contains(id)) continue;

                float distSq = (hornPos - tsai.transform.position).sqrMagnitude;
                if (distSq > radiusSq) continue;

                // Restore offset and remove from pullover — car resumes driving
                // Cooldown prevents immediate re-catch so car has time to drive forward
                var state = TSTrafficPulloverPatch._activePullovers[id];
                TSTrafficPulloverPatch.EnsureFieldRefsPublic();
                TSTrafficPulloverPatch.RestoreCarState(tsai, state);
                TSTrafficPulloverPatch._activePullovers.Remove(id);
                TSTrafficPulloverPatch._airhornCooldowns[id] = Time.time + 8f;
                flushed++;
            }

            if (flushed > 0)
                Plugin.Log($"[Airhorn] Flushed {flushed} stopped cars — they will re-attempt pullover");
        }
    }

    // =====================================================================
    // SIREN BROADCAST — hooks FFD_SirenControl.Update to trigger pullover
    // on nearby traffic when sirens are active.
    // =====================================================================
    [HarmonyPatch(typeof(FFD_SirenControl), "Update")]
    public static class SirenTrafficBroadcastPatch
    {
        private const float BroadcastInterval = 0.3f;
        private const float BroadcastRadius = 60f;

        private static float _lastBroadcast;
        private static bool _loggedFirst;
        private static float _lastDiagLog;

        [HarmonyPostfix]
        public static void Update_BroadcastToTraffic(FFD_SirenControl __instance)
        {
            // Diagnostic: log traffic system state every 5s
            if (Time.time - _lastDiagLog > 5f)
            {
                _lastDiagLog = Time.time;
                int tmuCount = TrafficMatrixUpdater.instance != null && TrafficMatrixUpdater.instance.activeCars != null
                    ? TrafficMatrixUpdater.instance.activeCars.Count : -1;
                int tsCount = TrafficSpawner.instance != null && TrafficSpawner.instance.activeCars != null
                    ? TrafficSpawner.instance.activeCars.Count : -1;
                int tsAICount = TSTrafficAI.trafficAIList != null ? TSTrafficAI.trafficAIList.Count : -1;
                int tsPulling = TSTrafficPulloverPatch._activePullovers.Count;
                Plugin.Log($"[Pullover-Diag] Siren={__instance.SirenState_Current} TMU={tmuCount} TS={tsCount} TSAI={tsAICount} pulling={tsPulling} obj={__instance.gameObject.name}");
            }

            if (__instance.SirenState_Current == FFD_SirenControl.SirenState.Off)
                return;

            if (Time.time - _lastBroadcast < BroadcastInterval)
                return;
            _lastBroadcast = Time.time;

            Vector3 sirenPos = __instance.transform.position;
            float radiusSq = BroadcastRadius * BroadcastRadius;
            int bakedCount = 0;
            int tsaiCount = 0;

            // --- Baked traffic (TrafficCarBaked) ---
            if (TrafficMatrixUpdater.instance != null && TrafficMatrixUpdater.instance.activeCars != null
                && TrafficMatrixUpdater.instance.activeCars.Count > 0)
            {
                var cars = TrafficMatrixUpdater.instance.activeCars;
                for (int i = 0; i < cars.Count; i++)
                {
                    var car = cars[i];
                    if (car == null) continue;
                    float distSq = (sirenPos - car.transform.position).sqrMagnitude;
                    if (distSq < radiusSq)
                    {
                        TrafficPulloverPatches.StartPullover(car);
                        bakedCount++;
                    }
                }
            }
            else if (TrafficSpawner.instance != null && TrafficSpawner.instance.activeCars != null
                && TrafficSpawner.instance.activeCars.Count > 0)
            {
                var gos = TrafficSpawner.instance.activeCars;
                for (int i = 0; i < gos.Count; i++)
                {
                    var go = gos[i];
                    if (go == null) continue;
                    float distSq = (sirenPos - go.transform.position).sqrMagnitude;
                    if (distSq < radiusSq)
                    {
                        var car = go.GetComponent<TrafficCarBaked>();
                        if (car != null)
                        {
                            TrafficPulloverPatches.StartPullover(car);
                            bakedCount++;
                        }
                    }
                }
            }

            // --- TS traffic (TSTrafficAI) — natural pullover via myLaneOffset ---
            if (TSTrafficAI.trafficAIList != null && TSTrafficAI.trafficAIList.Count > 0)
            {
                for (int i = 0; i < TSTrafficAI.trafficAIList.Count; i++)
                {
                    var tsai = TSTrafficAI.trafficAIList[i];
                    if (tsai == null) continue;
                    float distSq = (sirenPos - tsai.transform.position).sqrMagnitude;
                    if (distSq < radiusSq)
                    {
                        TSTrafficPulloverPatch.StartTSPullover(tsai);
                        tsaiCount++;
                    }
                }
                // Resume is handled internally by TSTrafficPulloverPatch state machine
                // (no more canMove = true/false — that was triggering Destroy!)
            }

            if (!_loggedFirst && (bakedCount > 0 || tsaiCount > 0))
            {
                _loggedFirst = true;
                Plugin.Log($"[Pullover] First broadcast: siren={__instance.SirenState_Current}, baked={bakedCount} tsai={tsaiCount}");
            }
        }

        public static bool IsAnySirenNearby(Vector3 position, float radius)
        {
            float radiusSq = radius * radius;
            var sirens = Object.FindObjectsOfType<FFD_SirenControl>();
            for (int i = 0; i < sirens.Length; i++)
            {
                if (sirens[i] == null) continue;
                if (sirens[i].SirenState_Current == FFD_SirenControl.SirenState.Off)
                    continue;
                if ((position - sirens[i].transform.position).sqrMagnitude < radiusSq)
                    return true;
            }
            return false;
        }
    }

    // =====================================================================
    // COLLISION PROTECTION — prevent TS cars from despawning on collision
    // during pullover. The original OnCollisionEnter in TSSimpleCar
    // instantly deactivates cars that touch parked cars, signs, etc.
    // =====================================================================
    [HarmonyPatch(typeof(TSSimpleCar), "OnCollisionEnter")]
    public static class TSCollisionProtectionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(TSSimpleCar __instance)
        {
            // Check if this car's TSTrafficAI is in our pullover system
            var tsai = __instance.GetComponent<TSTrafficAI>();
            if (tsai != null && TSTrafficPulloverPatch._activePullovers.ContainsKey(tsai.GetInstanceID()))
            {
                // Skip collision despawn — car is pulling over, minor bumps are expected
                return false;
            }
            return true;
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
