using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Temporary diagnostics to help identify Training Map scene contents and verify
    /// that scenario button clicks are being received by the correct EmergencyEvent.
    ///
    /// SCENE DUMP: Patches ProceduralEmergencySystem.Start to log every root GameObject
    /// and its world position when running on the Training Map (no Brooklyn, no Montgomery).
    /// Look for parking lot objects by their position cluster in CommunityFixes_diag.log.
    ///
    /// CLICK LOG: Patches EmergencyEvent.ForceThisCall (called by ForceCall.Force when a
    /// scenario button is clicked) to log the event name, type, and whether Procedural is
    /// set so we can confirm what the game sees when a button is pressed.
    /// </summary>

    // --- Scene dump on Training Map load ---
    [HarmonyPatch(typeof(ProceduralEmergencySystem), "Start")]
    public static class TrainingMapSceneDumpPatch
    {
        [HarmonyPostfix]
        public static void DumpSceneObjects(ProceduralEmergencySystem __instance)
        {
            // Only dump on Training Map (no Brooklyn flag, no Montgomery flag)
            if (__instance.Brooklyn || __instance.Montgomery)
                return;

            Plugin.Log("=== [Diag] Training Map scene dump ===");
            Plugin.Log($"  ProceduralEmergencySystem GO: '{__instance.gameObject.name}' pos={__instance.transform.position}");
            Plugin.Log($"  Brooklyn={__instance.Brooklyn} Montgomery={__instance.Montgomery}");
            Plugin.Log($"  CarFirePositions array length: {(__instance.CarFirePositions != null ? __instance.CarFirePositions.Length.ToString() : "NULL")}");

            // Dump all root GameObjects with positions
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            Plugin.Log($"  Root GameObjects in scene: {roots.Length}");
            foreach (var root in roots)
            {
                Plugin.Log($"    [root] '{root.name}' pos={root.transform.position} active={root.activeSelf}");

                // One level deeper for anything that looks parking/vehicle related
                foreach (Transform child in root.transform)
                {
                    string n = child.name.ToLower();
                    if (n.Contains("park") || n.Contains("car") || n.Contains("vehicle") ||
                        n.Contains("lot")  || n.Contains("fire") || n.Contains("spawn") ||
                        n.Contains("pos")  || n.Contains("point"))
                    {
                        Plugin.Log($"      [child] '{child.name}' pos={child.position} active={child.gameObject.activeSelf}");
                    }
                }
            }

            // Summarise connector objects found (or not found)
            var carFirePos      = Object.FindObjectOfType<CarFirePositions>();
            var parkedCar       = Object.FindObjectsOfType<ParkedCar>();
            var parkedVehicles  = Object.FindObjectOfType<ParkedVehiclesConnector>();
            var trafficBaked    = Object.FindObjectsOfType<TrafficCarBaked>();

            Plugin.Log($"  CarFirePositions connector: {(carFirePos != null ? "FOUND" : "NOT FOUND")}");
            Plugin.Log($"  ParkedCar objects: {parkedCar.Length}");
            Plugin.Log($"  ParkedVehiclesConnector: {(parkedVehicles != null ? "FOUND" : "NOT FOUND")}");
            Plugin.Log($"  TrafficCarBaked objects: {trafficBaked.Length}");

            if (trafficBaked.Length > 0)
            {
                Plugin.Log("  TrafficCarBaked positions:");
                foreach (var t in trafficBaked)
                    Plugin.Log($"    '{t.name}' pos={t.transform.position}");
            }

            Plugin.Log("=== [Diag] End scene dump ===");
        }
    }

    // --- Log every scenario button click ---
    [HarmonyPatch(typeof(EmergencyEvent), nameof(EmergencyEvent.ForceThisCall))]
    public static class ScenarioClickDiagPatch
    {
        [HarmonyPrefix]
        public static void LogClick(EmergencyEvent __instance)
        {
            var mgr = __instance.GetComponentInParent<EmergencyManagerV2>();
            string mgrName = mgr != null ? mgr.name : "NULL";
            bool hasProc   = mgr != null && mgr.Procedural != null;
            int carFireLen = (hasProc && mgr.Procedural.CarFirePositions != null)
                             ? mgr.Procedural.CarFirePositions.Length : -1;

            Plugin.Log($"[Diag-Click] ForceThisCall fired");
            Plugin.Log($"  Event name='{__instance.name}' eventType='{__instance.eventType}' show={__instance.show}");
            Plugin.Log($"  canLoadNewEmergency={EmergencyEvent.canLoadNewEmergency}");
            Plugin.Log($"  EmergencyManagerV2='{mgrName}' Procedural={(hasProc ? "SET" : "NULL")}");
            Plugin.Log($"  CarFirePositions.Length={carFireLen}");
            Plugin.Log($"  eventObjects count={(__instance.eventObjects != null ? __instance.eventObjects.Length.ToString() : "NULL")}");
        }
    }
}
