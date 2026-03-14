using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.PoliceEnhancement.Patches
{
    /// <summary>
    /// Injects police dispatch events into EmergencyManagerV2 so police calls
    /// appear in the dispatch rotation alongside fire and EMS.
    /// </summary>
    [HarmonyPatch(typeof(EmergencyManagerV2), "Awake")]
    public static class EmergencyManagerV2PoliceAwakePatch
    {
        private const int POLICE_EVENT_TARGET_COUNT = 3;

        private static readonly string[] PoliceEventNames = new[]
        {
            "Police MVA Response",
            "Police Disturbance",
            "Police Wellness Check"
        };

        [HarmonyPrefix]
        public static void Prefix(EmergencyManagerV2 __instance)
        {
            if (__instance.energencyEvents == null) return;

            int policeCount = 0;
            for (int i = 0; i < __instance.energencyEvents.Count; i++)
            {
                if (__instance.energencyEvents[i] != null &&
                    __instance.energencyEvents[i].name.Contains("Police"))
                    policeCount++;
            }

            if (policeCount >= POLICE_EVENT_TARGET_COUNT)
            {
                Plugin.Log($"[Police-Inject] Found {policeCount} existing police events, no injection needed");
                return;
            }

            int toAdd = POLICE_EVENT_TARGET_COUNT - policeCount;
            for (int i = 0; i < toAdd; i++)
            {
                int idx = policeCount + i;
                string eventName = idx < PoliceEventNames.Length
                    ? PoliceEventNames[idx]
                    : $"Police Call {idx + 1}";

                // Create disabled so AddComponent doesn't trigger Awake() before we init fields
                var go = new GameObject($"Police_Procedural_{idx + 1}");
                go.SetActive(false);
                go.transform.SetParent(__instance.transform);

                var ev = go.AddComponent<EmergencyEvent>();
                ev.eventName = eventName;
                ev.eventType = "Police";
                ev.show = true;

                // Initialize all array fields to empty to prevent NREs
                ev.eventObjects = new GameObject[0];
                ev.eventRoofs = new GameObject[0];
                ev.eventObjectsDynamic = new GameObject[0];
                ev.gasLeaks = new GameObject[0];
                ev.waterLeaks = new GameObject[0];

                // Initialize List fields that Awake() accesses (EnginePositions.Count etc.)
                ev.EnginePositions = new List<GameObject>();
                ev.LadderPositions = new List<GameObject>();
                ev.RescuePositions = new List<GameObject>();
                ev.BattalionPositions = new List<GameObject>();

                // Now safe to enable — Awake() will run with initialized fields
                go.SetActive(true);

                __instance.energencyEvents.Add(ev);
                Plugin.Log($"[Police-Inject] Added event: {eventName}");
            }

            Plugin.Log($"[Police-Inject] Injected {toAdd} police events (total now: {policeCount + toAdd})");
        }
    }
}
