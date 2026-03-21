using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// GTA V-style emergency vehicle yielding for TSTrafficAI.
    ///
    /// Cars detect sirens (fire/police/EMS), determine emergency vehicle direction,
    /// use the built-in GetOutOfLane() for proper right-side lane changes,
    /// adjust speed to merge around adjacent traffic, and crawl on the shoulder
    /// until the emergency vehicle passes.
    /// </summary>

    // =====================================================================
    // EMERGENCY VEHICLE REGISTRY — tracks all active sirens centrally
    // =====================================================================
    public static class EmergencyVehicleRegistry
    {
        public class SirenEntry
        {
            public Transform transform;
            public Rigidbody rigidbody;
            public float lastUpdated;
            public bool isAirhorn;

            public Vector3 Velocity =>
                rigidbody != null ? rigidbody.velocity : Vector3.zero;

            public Vector3 Position =>
                transform != null ? transform.position : Vector3.zero;
        }

        internal static readonly Dictionary<int, SirenEntry> _activeSirens =
            new Dictionary<int, SirenEntry>();

        public static void Register(Transform t)
        {
            int id = t.GetInstanceID();
            if (!_activeSirens.TryGetValue(id, out var entry))
            {
                entry = new SirenEntry
                {
                    transform = t,
                    rigidbody = t.GetComponentInParent<Rigidbody>(),
                    isAirhorn = false
                };
                _activeSirens[id] = entry;
            }
            entry.lastUpdated = Time.time;
        }

        public static void Unregister(Transform t)
        {
            _activeSirens.Remove(t.GetInstanceID());
        }

        public static void SetAirhorn(Transform t, bool active)
        {
            int id = t.GetInstanceID();
            if (_activeSirens.TryGetValue(id, out var entry))
                entry.isAirhorn = active;
        }

        /// <summary>
        /// Find nearest active siren within radius. Returns null if none.
        /// </summary>
        public static SirenEntry GetNearestSiren(Vector3 position, float maxRadius)
        {
            float bestDistSq = maxRadius * maxRadius;
            SirenEntry best = null;

            foreach (var kvp in _activeSirens)
            {
                var entry = kvp.Value;
                if (entry.transform == null) continue;
                // Stale check — remove entries not updated in 2s
                if (Time.time - entry.lastUpdated > 2f) continue;

                float distSq = (position - entry.Position).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = entry;
                }
            }
            return best;
        }

        public static bool IsAnyAirhornNearby(Vector3 position, float radius)
        {
            float radiusSq = radius * radius;
            foreach (var kvp in _activeSirens)
            {
                var entry = kvp.Value;
                if (entry.transform == null || !entry.isAirhorn) continue;
                if ((position - entry.Position).sqrMagnitude < radiusSq)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Clean stale entries. Called periodically from broadcaster.
        /// </summary>
        public static void CleanStale()
        {
            var toRemove = new List<int>();
            foreach (var kvp in _activeSirens)
            {
                if (kvp.Value.transform == null || Time.time - kvp.Value.lastUpdated > 2f)
                    toRemove.Add(kvp.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
                _activeSirens.Remove(toRemove[i]);
        }
    }

    // =====================================================================
    // YIELD CONSTANTS
    // =====================================================================
    public static class YieldConstants
    {
        // Broadcast
        public const float BroadcastInterval = 0.3f;
        public const float BroadcastRadiusBehind = 80f;
        public const float BroadcastRadiusBeside = 60f;
        public const float BroadcastRadiusAhead = 40f;
        public const float BroadcastRadiusOpposite = 50f;
        public const float DefaultBroadcastRadius = 60f;

        // Speed modifiers (multipliers on originalMAXSPEED)
        public const float SpeedPrepareBehind = 0.50f;
        public const float SpeedPrepareBeside = 0.60f;
        public const float SpeedPrepareAhead = 0.80f;
        public const float SpeedPrepareOpposite = 0.70f;
        public const float SpeedCrawl = 0.30f;
        public const float SpeedMergeSpeedUp = 1.30f;
        public const float SpeedMergeSlowDown = 0.40f;

        // Lane offset for rightmost lane shoulder
        public const float ShoulderOffset = 1.0f;
        public const float OffsetApplyRate = 0.8f;
        public const float OffsetRemoveRate = 0.5f;

        // Timing
        public const float DetectDuration = 0.6f;
        public const float MergeTimeout = 3.0f;
        public const float MergeRetryInterval = 0.6f;
        public const int MaxMergeAttempts = 3;
        public const float HoldTimeAfterClear = 4.0f;
        public const float AirhornFlushDuration = 3.0f;
        public const float ResumeDuration = 2.0f;
        public const float CooldownDuration = 5.0f;
        public const float SafetyTimeoutYield = 30.0f;
    }

    // =====================================================================
    // YIELD STATE TYPES
    // =====================================================================
    public enum YieldPhase
    {
        Detecting,
        PreparingToMerge,
        Merging,
        SlowingInRightLane,
        Yielding,
        AirhornFlush,
        Resuming,
        Cooldown
    }

    public enum RelativeDirection
    {
        Behind,
        Beside,
        Ahead,
        Opposite
    }

    public class YieldState
    {
        public YieldPhase phase;
        public float phaseStartTime;
        public float sirenLastSeen;
        public float originalMAXSPEED;
        public float originalMyLaneOffset;
        public int startingLane;
        public RelativeDirection sirenDirection;
        public int mergeAttempts;
        public float lastMergeAttemptTime;
        public bool wasAlreadyRightmost;
        public float currentExtraOffset;
        public int preMergeLane;
    }

    // =====================================================================
    // YIELD STATE MANAGER — core state machine
    // =====================================================================
    public static class YieldStateManager
    {
        internal static readonly Dictionary<int, YieldState> _yieldStates =
            new Dictionary<int, YieldState>();

        private static AccessTools.FieldRef<TSTrafficAI, float> _myLaneOffsetRef;
        private static AccessTools.FieldRef<TSTrafficAI, float> _lastSetRef;
        private static AccessTools.FieldRef<TSTrafficAI, float> _MAXSPEEDRef;
        private static AccessTools.FieldRef<TSNavigation, float> _lastRequestRef;
        private static bool _refsInitialized;

        private static void EnsureFieldRefs()
        {
            if (_refsInitialized) return;
            _myLaneOffsetRef = AccessTools.FieldRefAccess<TSTrafficAI, float>("myLaneOffset");
            _lastSetRef = AccessTools.FieldRefAccess<TSTrafficAI, float>("lastSet");
            _MAXSPEEDRef = AccessTools.FieldRefAccess<TSTrafficAI, float>("MAXSPEED");
            _lastRequestRef = AccessTools.FieldRefAccess<TSNavigation, float>("lastRequest");
            _refsInitialized = true;
        }

        public static bool IsYielding(int instanceId)
        {
            return _yieldStates.ContainsKey(instanceId);
        }

        /// <summary>
        /// Begin or refresh yield for a TS car.
        /// </summary>
        public static void BeginYield(TSTrafficAI tsai, EmergencyVehicleRegistry.SirenEntry siren)
        {
            int id = tsai.GetInstanceID();

            if (_yieldStates.TryGetValue(id, out var existing))
            {
                // Already yielding — refresh siren timestamp
                existing.sirenLastSeen = Time.time;
                // If resuming or in cooldown, pull back to active yield
                if (existing.phase == YieldPhase.Resuming)
                {
                    existing.phase = YieldPhase.SlowingInRightLane;
                    existing.phaseStartTime = Time.time;
                }
                else if (existing.phase == YieldPhase.Cooldown)
                {
                    existing.phase = YieldPhase.Detecting;
                    existing.phaseStartTime = Time.time;
                }
                return;
            }

            EnsureFieldRefs();

            var nav = tsai.GetComponent<TSNavigation>();
            if (nav == null) return;

            bool isRightmost = nav.lanes[nav.currentLane].laneLinkRight == -1;

            var state = new YieldState
            {
                phase = YieldPhase.Detecting,
                phaseStartTime = Time.time,
                sirenLastSeen = Time.time,
                originalMAXSPEED = _MAXSPEEDRef(tsai),
                originalMyLaneOffset = _myLaneOffsetRef(tsai),
                startingLane = nav.currentLane,
                sirenDirection = ComputeDirection(tsai, siren),
                mergeAttempts = 0,
                lastMergeAttemptTime = 0f,
                wasAlreadyRightmost = isRightmost,
                currentExtraOffset = 0f,
                preMergeLane = nav.currentLane
            };

            _yieldStates[id] = state;
            Plugin.Log($"[Yield] Car {id} detected siren (dir={state.sirenDirection}, rightmost={isRightmost})");
        }

        /// <summary>
        /// Compute siren direction relative to traffic car.
        /// </summary>
        private static RelativeDirection ComputeDirection(TSTrafficAI car, EmergencyVehicleRegistry.SirenEntry siren)
        {
            Vector3 toSiren = (siren.Position - car.transform.position).normalized;
            Vector3 carForward = car.transform.forward;
            Vector3 sirenForward = siren.Velocity.sqrMagnitude > 1f
                ? siren.Velocity.normalized
                : siren.transform.forward;

            float facingDot = Vector3.Dot(carForward, sirenForward);
            float behindDot = Vector3.Dot(carForward, toSiren);

            // Opposite direction traffic
            if (facingDot < -0.3f)
                return RelativeDirection.Opposite;
            // Siren is behind car (approaching from rear)
            if (behindDot < -0.2f)
                return RelativeDirection.Behind;
            // Siren is ahead
            if (behindDot > 0.5f)
                return RelativeDirection.Ahead;
            // Beside
            return RelativeDirection.Beside;
        }

        /// <summary>
        /// Get speed multiplier based on siren direction.
        /// </summary>
        private static float GetSpeedMultiplier(RelativeDirection dir)
        {
            switch (dir)
            {
                case RelativeDirection.Behind: return YieldConstants.SpeedPrepareBehind;
                case RelativeDirection.Beside: return YieldConstants.SpeedPrepareBeside;
                case RelativeDirection.Ahead: return YieldConstants.SpeedPrepareAhead;
                case RelativeDirection.Opposite: return YieldConstants.SpeedPrepareOpposite;
                default: return 0.6f;
            }
        }

        /// <summary>
        /// Core state machine tick. Called from controllerAI prefix every ~0.3s.
        /// </summary>
        public static void Tick(TSTrafficAI tsai)
        {
            int id = tsai.GetInstanceID();
            if (!_yieldStates.TryGetValue(id, out var state))
                return;

            EnsureFieldRefs();
            var nav = tsai.GetComponent<TSNavigation>();
            if (nav == null) return;

            float dt = 0.3f;

            // Update direction if siren still active
            var nearestSiren = EmergencyVehicleRegistry.GetNearestSiren(
                tsai.transform.position, YieldConstants.DefaultBroadcastRadius);
            if (nearestSiren != null)
            {
                state.sirenDirection = ComputeDirection(tsai, nearestSiren);
            }

            switch (state.phase)
            {
                // -------------------------------------------------------
                case YieldPhase.Detecting:
                {
                    // Brief detection phase to determine direction
                    if (Time.time - state.phaseStartTime > YieldConstants.DetectDuration)
                    {
                        bool isRightmost = nav.lanes[nav.currentLane].laneLinkRight == -1;
                        if (isRightmost)
                        {
                            state.phase = YieldPhase.SlowingInRightLane;
                            state.phaseStartTime = Time.time;
                            Plugin.Log($"[Yield] Car {id} already rightmost → slowing");
                        }
                        else
                        {
                            state.phase = YieldPhase.PreparingToMerge;
                            state.phaseStartTime = Time.time;
                            Plugin.Log($"[Yield] Car {id} preparing to merge right");
                        }
                    }
                    // Start slowing down during detection
                    float mult = GetSpeedMultiplier(state.sirenDirection);
                    _MAXSPEEDRef(tsai) = state.originalMAXSPEED * mult;
                    break;
                }

                // -------------------------------------------------------
                case YieldPhase.PreparingToMerge:
                {
                    // Don't attempt merge on connectors (intersections)
                    if (nav.travelingOnConector || nav.changingLane)
                    {
                        _MAXSPEEDRef(tsai) = state.originalMAXSPEED * GetSpeedMultiplier(state.sirenDirection);
                        break;
                    }

                    // Check if we can merge right
                    bool rightLaneExists = nav.lanes[nav.currentLane].laneLinkRight != -1;
                    if (!rightLaneExists)
                    {
                        state.phase = YieldPhase.SlowingInRightLane;
                        state.phaseStartTime = Time.time;
                        Plugin.Log($"[Yield] Car {id} no right lane → slowing");
                        break;
                    }

                    // Attempt merge at intervals
                    if (Time.time - state.lastMergeAttemptTime > YieldConstants.MergeRetryInterval)
                    {
                        state.lastMergeAttemptTime = Time.time;
                        state.preMergeLane = nav.currentLane;

                        // Reset the 3s rate limit on GetOutOfLane so we can call it
                        _lastRequestRef(nav) = 0f;
                        // GetOutOfLane() tries RIGHT first via rightParalelLaneIndex
                        nav.GetOutOfLane();

                        // Check if lane actually changed
                        if (nav.currentLane != state.preMergeLane)
                        {
                            state.phase = YieldPhase.Merging;
                            state.phaseStartTime = Time.time;
                            Plugin.Log($"[Yield] Car {id} merging right (lane {state.preMergeLane}→{nav.currentLane})");
                            break;
                        }

                        state.mergeAttempts++;
                        Plugin.Log($"[Yield] Car {id} merge attempt {state.mergeAttempts} failed");

                        if (state.mergeAttempts >= YieldConstants.MaxMergeAttempts)
                        {
                            state.phase = YieldPhase.SlowingInRightLane;
                            state.phaseStartTime = Time.time;
                            Plugin.Log($"[Yield] Car {id} merge failed {state.mergeAttempts}x → slowing in lane");
                            break;
                        }
                    }

                    // Slow down while preparing
                    _MAXSPEEDRef(tsai) = state.originalMAXSPEED * GetSpeedMultiplier(state.sirenDirection);

                    // Timeout
                    if (Time.time - state.phaseStartTime > YieldConstants.MergeTimeout)
                    {
                        state.phase = YieldPhase.SlowingInRightLane;
                        state.phaseStartTime = Time.time;
                        Plugin.Log($"[Yield] Car {id} merge timeout → slowing");
                    }
                    break;
                }

                // -------------------------------------------------------
                case YieldPhase.Merging:
                {
                    // Wait for the lane change to complete
                    _MAXSPEEDRef(tsai) = state.originalMAXSPEED * GetSpeedMultiplier(state.sirenDirection);

                    bool mergeComplete = !nav.changingLane;
                    bool timedOut = Time.time - state.phaseStartTime > YieldConstants.MergeTimeout;

                    if (mergeComplete || timedOut)
                    {
                        // Check if we can merge further right
                        bool moreRight = nav.lanes[nav.currentLane].laneLinkRight != -1;
                        if (moreRight && nearestSiren != null)
                        {
                            // Try another merge
                            state.phase = YieldPhase.PreparingToMerge;
                            state.phaseStartTime = Time.time;
                            state.mergeAttempts = 0;
                            Plugin.Log($"[Yield] Car {id} merge complete, trying further right");
                        }
                        else
                        {
                            state.phase = YieldPhase.SlowingInRightLane;
                            state.phaseStartTime = Time.time;
                            Plugin.Log($"[Yield] Car {id} merge complete → slowing (rightmost)");
                        }
                    }
                    break;
                }

                // -------------------------------------------------------
                case YieldPhase.SlowingInRightLane:
                {
                    // Crawl speed — keep moving, don't freeze
                    _MAXSPEEDRef(tsai) = state.originalMAXSPEED * YieldConstants.SpeedCrawl;

                    // Apply small shoulder offset
                    state.currentExtraOffset = Mathf.MoveTowards(
                        state.currentExtraOffset, YieldConstants.ShoulderOffset,
                        YieldConstants.OffsetApplyRate * dt);
                    _myLaneOffsetRef(tsai) = state.originalMyLaneOffset + state.currentExtraOffset;

                    // Check if siren has passed (now ahead or far)
                    if (nearestSiren != null)
                    {
                        state.sirenLastSeen = Time.time;
                        Vector3 toSiren = (nearestSiren.Position - tsai.transform.position).normalized;
                        float behindDot = Vector3.Dot(tsai.transform.forward, toSiren);
                        // Siren is now ahead of us — it passed
                        if (behindDot > 0.3f)
                        {
                            state.phase = YieldPhase.Yielding;
                            state.phaseStartTime = Time.time;
                            Plugin.Log($"[Yield] Car {id} siren passed → holding yield");
                        }
                    }
                    else
                    {
                        // No siren nearby — check if it cleared
                        if (Time.time - state.sirenLastSeen > YieldConstants.HoldTimeAfterClear)
                        {
                            state.phase = YieldPhase.Resuming;
                            state.phaseStartTime = Time.time;
                            Plugin.Log($"[Yield] Car {id} siren gone → resuming");
                        }
                    }

                    // Safety timeout
                    if (Time.time - state.phaseStartTime > YieldConstants.SafetyTimeoutYield)
                    {
                        state.phase = YieldPhase.Resuming;
                        state.phaseStartTime = Time.time;
                        Plugin.Log($"[Yield] Car {id} safety timeout → resuming");
                    }
                    break;
                }

                // -------------------------------------------------------
                case YieldPhase.Yielding:
                {
                    // Hold position, slightly increase speed
                    _MAXSPEEDRef(tsai) = state.originalMAXSPEED * 0.5f;
                    _myLaneOffsetRef(tsai) = state.originalMyLaneOffset + state.currentExtraOffset;

                    // Refresh siren time if still nearby
                    if (nearestSiren != null)
                        state.sirenLastSeen = Time.time;

                    // Resume when sirens clear
                    if (Time.time - state.sirenLastSeen > YieldConstants.HoldTimeAfterClear)
                    {
                        state.phase = YieldPhase.Resuming;
                        state.phaseStartTime = Time.time;
                        Plugin.Log($"[Yield] Car {id} hold complete → resuming");
                    }
                    break;
                }

                // -------------------------------------------------------
                case YieldPhase.AirhornFlush:
                {
                    // Drive forward at full speed
                    _MAXSPEEDRef(tsai) = state.originalMAXSPEED;
                    // Quickly remove offset
                    state.currentExtraOffset = Mathf.MoveTowards(
                        state.currentExtraOffset, 0f, YieldConstants.OffsetRemoveRate * dt * 3f);
                    _myLaneOffsetRef(tsai) = state.originalMyLaneOffset + state.currentExtraOffset;

                    if (Time.time - state.phaseStartTime > YieldConstants.AirhornFlushDuration)
                    {
                        state.phase = YieldPhase.Cooldown;
                        state.phaseStartTime = Time.time;
                        Plugin.Log($"[Yield] Car {id} airhorn flush done → cooldown");
                    }
                    break;
                }

                // -------------------------------------------------------
                case YieldPhase.Resuming:
                {
                    // Gradually restore speed and offset
                    float resumeProgress = Mathf.Clamp01(
                        (Time.time - state.phaseStartTime) / YieldConstants.ResumeDuration);

                    _MAXSPEEDRef(tsai) = Mathf.Lerp(
                        state.originalMAXSPEED * YieldConstants.SpeedCrawl,
                        state.originalMAXSPEED,
                        resumeProgress);

                    state.currentExtraOffset = Mathf.MoveTowards(
                        state.currentExtraOffset, 0f, YieldConstants.OffsetRemoveRate * dt);
                    _myLaneOffsetRef(tsai) = state.originalMyLaneOffset + state.currentExtraOffset;

                    // Force canMove true during resume
                    _lastSetRef(tsai) = Time.time - 3f;

                    if (resumeProgress >= 1f && state.currentExtraOffset <= 0.05f)
                    {
                        _myLaneOffsetRef(tsai) = state.originalMyLaneOffset;
                        _MAXSPEEDRef(tsai) = state.originalMAXSPEED;
                        state.phase = YieldPhase.Cooldown;
                        state.phaseStartTime = Time.time;
                        Plugin.Log($"[Yield] Car {id} resumed → cooldown");
                    }
                    break;
                }

                // -------------------------------------------------------
                case YieldPhase.Cooldown:
                {
                    // No modifications — let car drive normally
                    if (Time.time - state.phaseStartTime > YieldConstants.CooldownDuration)
                    {
                        _yieldStates.Remove(id);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Trigger airhorn flush on all yielding cars within range.
        /// </summary>
        public static int FlushForAirhorn(Vector3 hornPos, float radius)
        {
            float radiusSq = radius * radius;
            int flushed = 0;
            var toFlush = new List<KeyValuePair<int, TSTrafficAI>>();

            if (TSTrafficAI.trafficAIList == null) return 0;

            for (int i = 0; i < TSTrafficAI.trafficAIList.Count; i++)
            {
                var tsai = TSTrafficAI.trafficAIList[i];
                if (tsai == null) continue;
                int id = tsai.GetInstanceID();

                if (!_yieldStates.TryGetValue(id, out var state)) continue;
                // Only flush cars that are actively stopped/slowed
                if (state.phase != YieldPhase.SlowingInRightLane &&
                    state.phase != YieldPhase.Yielding)
                    continue;

                float distSq = (hornPos - tsai.transform.position).sqrMagnitude;
                if (distSq > radiusSq) continue;

                toFlush.Add(new KeyValuePair<int, TSTrafficAI>(id, tsai));
            }

            foreach (var kvp in toFlush)
            {
                var state = _yieldStates[kvp.Key];
                state.phase = YieldPhase.AirhornFlush;
                state.phaseStartTime = Time.time;
                flushed++;
            }

            return flushed;
        }

        /// <summary>
        /// Clean up when a car is disabled.
        /// </summary>
        public static void Cleanup(TSTrafficAI tsai)
        {
            int id = tsai.GetInstanceID();
            if (_yieldStates.TryGetValue(id, out var state))
            {
                EnsureFieldRefs();
                _myLaneOffsetRef(tsai) = state.originalMyLaneOffset;
                _yieldStates.Remove(id);
            }
        }
    }

    // =====================================================================
    // HARMONY PATCHES
    // =====================================================================

    /// <summary>
    /// Register fire/EMS sirens in the EmergencyVehicleRegistry.
    /// </summary>
    [HarmonyPatch(typeof(FFD_SirenControl), "Update")]
    public static class SirenRegistryPatch
    {
        private const float BroadcastInterval = 0.3f;
        private static float _lastBroadcast;
        private static float _lastClean;

        [HarmonyPostfix]
        public static void Update_Register(FFD_SirenControl __instance)
        {
            // Register/unregister this siren
            if (__instance.SirenState_Current != FFD_SirenControl.SirenState.Off)
                EmergencyVehicleRegistry.Register(__instance.transform);
            else
                EmergencyVehicleRegistry.Unregister(__instance.transform);

            // Periodic broadcast to traffic (only from one siren per tick)
            if (Time.time - _lastBroadcast < BroadcastInterval)
                return;
            _lastBroadcast = Time.time;

            // Clean stale entries every 5s
            if (Time.time - _lastClean > 5f)
            {
                _lastClean = Time.time;
                EmergencyVehicleRegistry.CleanStale();
            }

            // Broadcast to nearby TS traffic
            BroadcastToTraffic();
        }

        internal static void BroadcastToTraffic()
        {
            if (EmergencyVehicleRegistry._activeSirens.Count == 0) return;
            if (TSTrafficAI.trafficAIList == null || TSTrafficAI.trafficAIList.Count == 0) return;

            // For each active siren, find nearby traffic cars
            foreach (var sirenKvp in EmergencyVehicleRegistry._activeSirens)
            {
                var siren = sirenKvp.Value;
                if (siren.transform == null) continue;

                Vector3 sirenPos = siren.Position;

                for (int i = 0; i < TSTrafficAI.trafficAIList.Count; i++)
                {
                    var tsai = TSTrafficAI.trafficAIList[i];
                    if (tsai == null) continue;

                    float distSq = (sirenPos - tsai.transform.position).sqrMagnitude;
                    float radiusSq = YieldConstants.DefaultBroadcastRadius *
                                     YieldConstants.DefaultBroadcastRadius;

                    if (distSq < radiusSq)
                    {
                        YieldStateManager.BeginYield(tsai, siren);
                    }
                }
            }

            // Also broadcast for baked traffic (keep existing system)
            BroadcastToBakedTraffic();
        }

        private static void BroadcastToBakedTraffic()
        {
            foreach (var sirenKvp in EmergencyVehicleRegistry._activeSirens)
            {
                var siren = sirenKvp.Value;
                if (siren.transform == null) continue;
                Vector3 sirenPos = siren.Position;
                float radiusSq = YieldConstants.DefaultBroadcastRadius *
                                 YieldConstants.DefaultBroadcastRadius;

                if (TrafficMatrixUpdater.instance != null &&
                    TrafficMatrixUpdater.instance.activeCars != null &&
                    TrafficMatrixUpdater.instance.activeCars.Count > 0)
                {
                    var cars = TrafficMatrixUpdater.instance.activeCars;
                    for (int i = 0; i < cars.Count; i++)
                    {
                        var car = cars[i];
                        if (car == null) continue;
                        if ((sirenPos - car.transform.position).sqrMagnitude < radiusSq)
                            TrafficPulloverPatches.StartPullover(car);
                    }
                }
                else if (TrafficSpawner.instance != null &&
                         TrafficSpawner.instance.activeCars != null &&
                         TrafficSpawner.instance.activeCars.Count > 0)
                {
                    var gos = TrafficSpawner.instance.activeCars;
                    for (int i = 0; i < gos.Count; i++)
                    {
                        var go = gos[i];
                        if (go == null) continue;
                        if ((sirenPos - go.transform.position).sqrMagnitude < radiusSq)
                        {
                            var car = go.GetComponent<TrafficCarBaked>();
                            if (car != null)
                                TrafficPulloverPatches.StartPullover(car);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Register NYPD police sirens in the EmergencyVehicleRegistry.
    /// </summary>
    [HarmonyPatch(typeof(NYPDSirenController), "Update")]
    public static class NYPDSirenRegistryPatch
    {
        [HarmonyPostfix]
        public static void Update_Register(NYPDSirenController __instance)
        {
            bool sirenActive = __instance.wailing || __instance.yelping || __instance.prtying;
            if (sirenActive)
                EmergencyVehicleRegistry.Register(__instance.transform);
            else
                EmergencyVehicleRegistry.Unregister(__instance.transform);
        }
    }

    /// <summary>
    /// Per-car yield state machine, runs in controllerAI prefix.
    /// </summary>
    [HarmonyPatch(typeof(TSTrafficAI), "controllerAI")]
    public static class YieldControllerAIPatch
    {
        [HarmonyPrefix]
        public static void Prefix(TSTrafficAI __instance)
        {
            YieldStateManager.Tick(__instance);
        }
    }

    /// <summary>
    /// Block normal AI lane changes during active yield (prevents fighting).
    /// </summary>
    [HarmonyPatch(typeof(TSNavigation), "LaneChange")]
    public static class YieldLaneChangeGuardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(TSNavigation __instance)
        {
            // Allow the call if the car isn't yielding
            var tsai = __instance.GetComponent<TSTrafficAI>();
            if (tsai == null) return true;

            int id = tsai.GetInstanceID();
            if (!YieldStateManager._yieldStates.TryGetValue(id, out var state))
                return true; // Not yielding — let normal AI work

            // Only allow lane changes during merge-related phases
            // (our code calls GetOutOfLane which internally calls LaneChange)
            if (state.phase == YieldPhase.PreparingToMerge ||
                state.phase == YieldPhase.Merging)
                return true;

            // Block all other lane changes during yield
            return false;
        }
    }

    /// <summary>
    /// Air horn triggers flush on nearby yielding cars.
    /// </summary>
    [HarmonyPatch(typeof(FFD_Airhorn), "OnPlay")]
    public static class YieldAirhornPatch
    {
        [HarmonyPostfix]
        public static void OnPlay_Flush(FFD_Airhorn __instance)
        {
            if (__instance.car == null) return;
            Vector3 hornPos = __instance.car.transform.position;

            // Set airhorn flag in registry
            EmergencyVehicleRegistry.SetAirhorn(__instance.transform, true);

            int flushed = YieldStateManager.FlushForAirhorn(hornPos, 40f);
            if (flushed > 0)
                Plugin.Log($"[Yield-Horn] Flushed {flushed} yielding cars");
        }
    }

    /// <summary>
    /// Clear airhorn flag when horn stops.
    /// </summary>
    [HarmonyPatch(typeof(FFD_Airhorn), "OnStop")]
    public static class YieldAirhornStopPatch
    {
        [HarmonyPostfix]
        public static void OnStop_Clear(FFD_Airhorn __instance)
        {
            EmergencyVehicleRegistry.SetAirhorn(__instance.transform, false);
        }
    }

    /// <summary>
    /// Collision protection — skip despawn during active yield.
    /// </summary>
    [HarmonyPatch(typeof(TSSimpleCar), "OnCollisionEnter")]
    public static class YieldCollisionProtectionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(TSSimpleCar __instance)
        {
            var tsai = __instance.GetComponent<TSTrafficAI>();
            if (tsai != null && YieldStateManager.IsYielding(tsai.GetInstanceID()))
                return false; // Skip despawn
            return true;
        }
    }

    /// <summary>
    /// Clean up yield state when car is disabled.
    /// </summary>
    [HarmonyPatch(typeof(TSTrafficAI), "OnDisable")]
    public static class YieldOnDisableCleanup
    {
        [HarmonyPostfix]
        public static void Postfix(TSTrafficAI __instance)
        {
            YieldStateManager.Cleanup(__instance);
        }
    }
}
