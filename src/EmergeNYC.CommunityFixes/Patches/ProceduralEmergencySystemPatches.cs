using System.Collections;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for ProceduralEmergencySystem:
    /// 1. Start(): 12+ FindObjectOfType calls with no null checks. If any connector object
    ///    is missing from the scene, the member access (.AllBrownstones, .Apartments, etc.)
    ///    throws a NullReferenceException. Fix: null-check each FindObjectOfType result.
    /// 2. SpawnManholeFire(): CurrentEmergencyPos = currentcall.transform at line 541 is
    ///    outside the IsMasterClient block. On non-master clients, currentcall is null and
    ///    this NREs. Fix: replace the coroutine with one that moves the assignment inside the
    ///    IsMasterClient guard.
    /// 3. ClearCallRPC(): The else branch calls PhotonNetwork.Destroy(currentcall.gameObject)
    ///    but currentcall can be null on non-master clients (it was never set, or already
    ///    cleared). Fix: null-check currentcall before accessing .gameObject.
    /// </summary>
    [HarmonyPatch(typeof(ProceduralEmergencySystem))]
    public static class ProceduralEmergencySystemPatches
    {
        // Fix 1: Null-safe Start - wrap every FindObjectOfType in a null check
        [HarmonyPatch("Start")]
        [HarmonyPrefix]
        public static bool Start_NullSafeFindObjects(ProceduralEmergencySystem __instance)
        {
            var brooklyn = __instance.Brooklyn;
            var montgomery = __instance.Montgomery;

            if (!brooklyn && !montgomery)
            {
                var carFirePos = Object.FindObjectOfType<CarFirePositions>();
                if (carFirePos != null)
                {
                    __instance.CarFirePositions = carFirePos.CarFires;
                    Plugin.Log($"[CarFire] Loaded {carFirePos.CarFires.Length} positions from CarFirePositions connector");
                }
                else
                {
                    // Training Map fallback: no CarFirePositions connector in scene.
                    // Try to find ParkedCar objects and use their transforms instead.
                    var parkedCars = Object.FindObjectsOfType<ParkedCar>();
                    if (parkedCars != null && parkedCars.Length > 0)
                    {
                        var transforms = new Transform[parkedCars.Length];
                        for (int i = 0; i < parkedCars.Length; i++)
                            transforms[i] = parkedCars[i].transform;
                        __instance.CarFirePositions = transforms;
                        Plugin.Log($"[CarFire] No CarFirePositions connector — fell back to {transforms.Length} ParkedCar transforms");
                    }
                    else
                    {
                        // Last resort: try ParkedVehiclesConnector (Brooklyn-style)
                        var parkedVehicles = Object.FindObjectOfType<ParkedVehiclesConnector>();
                        if (parkedVehicles != null && parkedVehicles.spawns != null && parkedVehicles.spawns.Length > 0)
                        {
                            __instance.CarFirePositions = parkedVehicles.spawns;
                            Plugin.Log($"[CarFire] No CarFirePositions or ParkedCar — fell back to {parkedVehicles.spawns.Length} ParkedVehiclesConnector spawns");
                        }
                        else
                        {
                            Debug.LogWarning("[CommunityFixes] CarFirePositions: no CarFirePositions connector, no ParkedCar objects, and no ParkedVehiclesConnector found in scene — car fire will not spawn");
                        }
                    }
                }

                var manholePos = Object.FindObjectOfType<ManholeFirePositions>();
                if (manholePos != null)
                {
                    __instance.AllManholes = manholePos.AllManholes;
                    __instance.RoundManholeFires = manholePos.RoundManholeFires;
                }
                else
                {
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: ManholeFirePositions not found in scene");
                }

                var fireBoxHolder = Object.FindObjectOfType<FireBoxHolder>();
                if (fireBoxHolder != null)
                    __instance.boxes = fireBoxHolder.boxes;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: FireBoxHolder not found in scene");

                var brownstone = Object.FindObjectOfType<BrownStoneFireConnector>();
                if (brownstone != null)
                    __instance.BrownStoneFireSpawns = brownstone.AllBrownstones;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: BrownStoneFireConnector not found in scene");

                var apartment = Object.FindObjectOfType<ApartmentFireConnector>();
                if (apartment != null)
                    __instance.ApartmentFireSpawnPoints = apartment.Apartments;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: ApartmentFireConnector not found in scene");

                var garage = Object.FindObjectOfType<GarageFireLinker>();
                if (garage != null)
                    __instance.GarageFireSpawns = garage.Garages;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: GarageFireLinker not found in scene");

                var trailer = Object.FindObjectOfType<TrailerFireConnector>();
                if (trailer != null)
                    __instance.TrailerFires = trailer.Trailers;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: TrailerFireConnector not found in scene");

                var ems = Object.FindObjectOfType<EMSCallsConnector>();
                if (ems != null)
                    __instance.EMSCalls = ems.spawnpoints;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: EMSCallsConnector not found in scene");

                var vacantComm = Object.FindObjectOfType<VacantCommercialsConnector>();
                if (vacantComm != null)
                    __instance.VacantCommercials = vacantComm.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: VacantCommercialsConnector not found in scene");

                var vacantPD = Object.FindObjectOfType<VacantPDFireConnector>();
                if (vacantPD != null)
                    __instance.Vacant2stry = vacantPD.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: VacantPDFireConnector not found in scene");

                var whiteBrown = Object.FindObjectOfType<WhiteBrownStoneConnector>();
                if (whiteBrown != null)
                    __instance.WhiteBrownStones = whiteBrown.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: WhiteBrownStoneConnector not found in scene");
            }

            if (brooklyn && !montgomery)
            {
                var brownstone = Object.FindObjectOfType<BrownStoneFireConnector>();
                if (brownstone != null)
                    __instance.BrownStoneFireSpawns = brownstone.AllBrownstones;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: BrownStoneFireConnector not found in scene");

                var apartment = Object.FindObjectOfType<ApartmentFireConnector>();
                if (apartment != null)
                    __instance.ApartmentFireSpawnPoints = apartment.Apartments;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: ApartmentFireConnector not found in scene");

                var garage = Object.FindObjectOfType<GarageFireLinker>();
                if (garage != null)
                    __instance.GarageFireSpawns = garage.Garages;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: GarageFireLinker not found in scene");

                var trailer = Object.FindObjectOfType<TrailerFireConnector>();
                if (trailer != null)
                    __instance.TrailerFires = trailer.Trailers;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: TrailerFireConnector not found in scene");

                var ems = Object.FindObjectOfType<EMSCallsConnector>();
                if (ems != null)
                    __instance.EMSCalls = ems.spawnpoints;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: EMSCallsConnector not found in scene");

                var vacantComm = Object.FindObjectOfType<VacantCommercialsConnector>();
                if (vacantComm != null)
                    __instance.VacantCommercials = vacantComm.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: VacantCommercialsConnector not found in scene");

                var vacantPD = Object.FindObjectOfType<VacantPDFireConnector>();
                if (vacantPD != null)
                    __instance.Vacant2stry = vacantPD.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: VacantPDFireConnector not found in scene");

                var whiteBrown = Object.FindObjectOfType<WhiteBrownStoneConnector>();
                if (whiteBrown != null)
                    __instance.WhiteBrownStones = whiteBrown.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: WhiteBrownStoneConnector not found in scene");

                var parkedVehicles = Object.FindAnyObjectByType<ParkedVehiclesConnector>();
                if (parkedVehicles != null)
                    __instance.CarFirePositions = parkedVehicles.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: ParkedVehiclesConnector not found in scene");
            }

            if (montgomery)
            {
                var brownstone = Object.FindObjectOfType<BrownStoneFireConnector>();
                if (brownstone != null)
                    __instance.BrownStoneFireSpawns = brownstone.AllBrownstones;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: BrownStoneFireConnector not found in scene");

                var apartment = Object.FindObjectOfType<ApartmentFireConnector>();
                if (apartment != null)
                    __instance.ApartmentFireSpawnPoints = apartment.Apartments;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: ApartmentFireConnector not found in scene");

                var garage = Object.FindObjectOfType<GarageFireLinker>();
                if (garage != null)
                    __instance.GarageFireSpawns = garage.Garages;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: GarageFireLinker not found in scene");

                var trailer = Object.FindObjectOfType<TrailerFireConnector>();
                if (trailer != null)
                    __instance.TrailerFires = trailer.Trailers;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: TrailerFireConnector not found in scene");

                var ems = Object.FindObjectOfType<EMSCallsConnector>();
                if (ems != null)
                    __instance.EMSCalls = ems.spawnpoints;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: EMSCallsConnector not found in scene");

                var vacantComm = Object.FindObjectOfType<VacantCommercialsConnector>();
                if (vacantComm != null)
                    __instance.VacantCommercials = vacantComm.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: VacantCommercialsConnector not found in scene");

                var vacantPD = Object.FindObjectOfType<VacantPDFireConnector>();
                if (vacantPD != null)
                    __instance.Vacant2stry = vacantPD.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: VacantPDFireConnector not found in scene");

                var whiteBrown = Object.FindObjectOfType<WhiteBrownStoneConnector>();
                if (whiteBrown != null)
                    __instance.WhiteBrownStones = whiteBrown.spawns;
                else
                    Debug.LogWarning("[CommunityFixes] ProceduralEmergencySystem.Start: WhiteBrownStoneConnector not found in scene");
            }

            return false; // Skip original
        }

        // Fix 2: SpawnManholeFire - CurrentEmergencyPos assignment is outside the
        // IsMasterClient block, causing NRE on non-master clients where currentcall is null.
        // Replace the coroutine entirely with a fixed version.
        [HarmonyPatch(nameof(ProceduralEmergencySystem.SpawnManholeFire))]
        [HarmonyPrefix]
        public static bool SpawnManholeFire_FixNullRef(
            ProceduralEmergencySystem __instance,
            ref IEnumerator __result)
        {
            __result = FixedSpawnManholeFire(__instance);
            return false; // Skip original coroutine
        }

        private static IEnumerator FixedSpawnManholeFire(ProceduralEmergencySystem instance)
        {
            instance.callAmount = "1ST ALARM";

            if (instance.currentcall == null)
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    GameObject gameObject = instance.RoundManholeFires[
                        Random.Range(0, instance.RoundManholeFires.Length)];

                    if (instance.AllManholes.Contains(gameObject.transform))
                    {
                        instance.AllManholes.Remove(gameObject.transform);
                    }

                    instance.currentcall = PhotonNetwork.Instantiate(
                        "RoundManholeFire",
                        gameObject.transform.position,
                        gameObject.transform.rotation,
                        0);

                    // Fix: moved inside IsMasterClient block so currentcall is guaranteed non-null
                    instance.CurrentEmergencyPos = instance.currentcall.transform;
                    instance.GetAddress();
                    yield return new WaitForSeconds(0.5f);
                    instance.GetAssignment();
                }
            }
            else
            {
                // currentcall already exists - spawn an additional manhole fire nearby
                Transform transform = Traverse.Create(instance)
                    .Method("FindNearestManhole", instance.currentcall.transform.position)
                    .GetValue<Transform>(instance.currentcall.transform.position);

                if (transform != null)
                {
                    PhotonNetwork.Instantiate(
                        "RoundManholeFire",
                        transform.position,
                        transform.rotation,
                        0);

                    if (instance.AllManholes.Contains(transform))
                    {
                        instance.AllManholes.Remove(transform);
                    }
                }
            }
        }

        // Fix 3: ClearCallRPC - null-check currentcall before accessing .gameObject
        // On non-master clients, currentcall may never have been set or may already be null.
        [HarmonyPatch(nameof(ProceduralEmergencySystem.ClearCallRPC))]
        [HarmonyPrefix]
        public static bool ClearCallRPC_NullSafe(ProceduralEmergencySystem __instance)
        {
            __instance.text.text = "";

            if (!__instance.Montgomery)
            {
                __instance.TenEight.SetActive(true);
            }

            var puppet = Object.FindObjectOfType<RootMotion.Dynamics.PUNPuppet>();
            if (puppet != null)
            {
                puppet.gameObject.SetActive(false);
            }
            else if (__instance.currentcall != null)
            {
                PhotonNetwork.Destroy(__instance.currentcall.gameObject);
            }
            else
            {
                Debug.LogWarning("[CommunityFixes] ClearCallRPC: currentcall was already null, nothing to destroy");
            }

            __instance.currentcall = null;

            return false; // Skip original
        }
    }
}
