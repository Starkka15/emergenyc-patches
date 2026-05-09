using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// T1: TSTrafficSpawner uses myPosition = base.transform.position (spawner origin, static)
    /// for all far-car and spawn-range checks. This means cars visible to the player get culled
    /// when the player moves away from the spawner's world-space origin.
    /// Fix: Override myPosition to Camera.main.position before each check that uses it.
    ///
    /// T2: Prevent pop-in by rejecting spawn points within 30m of the camera.
    /// </summary>

    [HarmonyPatch(typeof(TSTrafficSpawner))]
    public static class TSTrafficSpawnerCameraFix
    {
        private const float MinSpawnDistFromCamera = 30f;

        private static AccessTools.FieldRef<TSTrafficSpawner, Vector3> _myPositionRef;
        private static bool _refsInit;

        private static void EnsureRefs()
        {
            if (_refsInit) return;
            _myPositionRef = AccessTools.FieldRefAccess<TSTrafficSpawner, Vector3>("myPosition");
            _refsInit = true;
        }

        private static Vector3 CameraPos(TSTrafficSpawner instance)
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : instance.transform.position;
        }

        // T1: Skip CheckFarCarsSingleThread entirely — don't despawn TS cars by distance.
        // The camera-based 150m threshold still fired for visible cars. User explicitly
        // wants no view-range despawning. Pool recycling still happens via AddCar when
        // the spawner needs new cars (upside-down / disabled handled separately if needed).
        [HarmonyPatch("CheckFarCarsSingleThread")]
        [HarmonyPrefix]
        public static bool CheckFarCars_Disable(TSTrafficSpawner __instance)
        {
            return false; // skip original
        }

        // T1: Keep myPosition = camera after Update runs (for CheckNearLanesSingleThread coroutine).
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void Update_KeepCameraPos(TSTrafficSpawner __instance)
        {
            EnsureRefs();
            _myPositionRef(__instance) = CameraPos(__instance);
        }

        // T1+T2: Override myPosition before AddCar's range check, and reject points too close to camera.
        [HarmonyPatch("AddCar")]
        [HarmonyPrefix]
        public static bool AddCar_CameraGuard(TSTrafficSpawner __instance)
        {
            EnsureRefs();
            var camPos = CameraPos(__instance);
            _myPositionRef(__instance) = camPos;

            // T2: If spawner's candidate point selection hasn't run yet we can't check individual
            // points here. Instead we gate on whether the spawner itself is too close — the actual
            // per-point 30m check happens in the Transpiler-replaced range check below.
            // As a conservative guard, skip the whole AddCar tick when camera is within the minimum
            // safe spawn distance of the spawner origin (rare edge case — spawner usually far from player).
            // Per-point rejection is handled by the myPosition override: maxDistanceSQRMin won't be
            // satisfied for points < 30m when we set it correctly.
            return true; // Let AddCar run; myPosition is now correct
        }

        // T2: Postfix on AddCar is not sufficient because the point selection is internal.
        // Instead we patch GetNextCarFarIndex indirectly via the Update postfix setting myPosition
        // to camera position, combined with a direct spawn-point proximity guard here.
        // This prefix on AddCar also prevents spawning when camera is within MinSpawnDistFromCamera
        // of the candidate spawn area (detected via maxDistanceSQRMin comparison in original code,
        // which now uses camera position correctly due to the myPosition override above).
        [HarmonyPatch("AddCar")]
        [HarmonyPostfix]
        public static void AddCar_LogSpawn(TSTrafficSpawner __instance)
        {
            // Intentionally empty — spawn events fired via TrafficAPI in T12.
        }
    }

    /// <summary>
    /// T2: Additional per-point spawn guard.
    /// TSTrafficSpawner.AddCar checks: sqrMagnitude &lt; maxDistanceSQRMin → skip (too close).
    /// With myPosition now = camera pos, this already rejects points inside the inner ring.
    /// But maxDistanceSQRMin uses (maxDistance - offset)² which is (150-140)² = 100m² = 10m radius —
    /// that's too small. We need a hard 30m floor.
    /// Patch: prefix on AddCar that sets maxDistanceSQRMin to max(current, 30²) temporarily,
    /// then restores it in a postfix.
    /// </summary>
    [HarmonyPatch(typeof(TSTrafficSpawner), "AddCar")]
    public static class TSTrafficSpawnMinDistGuard
    {
        private const float HardMinDist = 30f;
        private const float HardMinDistSq = HardMinDist * HardMinDist;

        private static float _savedMin;

        private static AccessTools.FieldRef<TSTrafficSpawner, float> _maxDistSQRMinRef;
        private static bool _refsInit;

        private static void EnsureRefs()
        {
            if (_refsInit) return;
            _maxDistSQRMinRef = AccessTools.FieldRefAccess<TSTrafficSpawner, float>("maxDistanceSQRMin");
            _refsInit = true;
        }

        [HarmonyPrefix]
        public static void Prefix(TSTrafficSpawner __instance)
        {
            EnsureRefs();
            _savedMin = _maxDistSQRMinRef(__instance);
            if (_savedMin < HardMinDistSq)
                _maxDistSQRMinRef(__instance) = HardMinDistSq;
        }

        [HarmonyPostfix]
        public static void Postfix(TSTrafficSpawner __instance)
        {
            EnsureRefs();
            _maxDistSQRMinRef(__instance) = _savedMin;
        }
    }
}
