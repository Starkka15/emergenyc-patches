using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for EmergencyManagerV2:
    /// 1. MainEmergencyLoop: Random.Range(0, count-1) never selects the last event (off-by-one)
    /// 2. MainEmergencyLoop: infinite loop when all events have been triggered (triggeredEmergencies
    ///    fills up, inner while loop spins forever)
    /// 3. ActivateEvent called 2-3 times per emergency (lines 988, 992/997) - double/triple activation
    /// 4. ReenableAllCalls crashes when events have show=false (missionbutton is null)
    /// 5. EMS events missing from scene data — inject synthetic EMS events into the rotation
    /// </summary>
    [HarmonyPatch(typeof(EmergencyManagerV2))]
    public static class EmergencyManagerV2Patches
    {
        private const int EMS_EVENT_TARGET_COUNT = 4;

        // Fix 5: Inject EMS events if none exist in the scene.
        // Runs before Awake so the events are in the list when InstanceMissions() and
        // MainEmergencyLoop are called.
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        public static void Awake_InjectEMSEvents(EmergencyManagerV2 __instance)
        {
            if (__instance.energencyEvents == null)
                return;

            // Count existing EMS events
            int emsCount = 0;
            for (int i = 0; i < __instance.energencyEvents.Count; i++)
            {
                if (__instance.energencyEvents[i] != null &&
                    __instance.energencyEvents[i].name.Contains("EMS"))
                {
                    emsCount++;
                }
            }

            if (emsCount >= EMS_EVENT_TARGET_COUNT)
            {
                Plugin.Log($"[EMS-Inject] Found {emsCount} existing EMS events, no injection needed");
                return;
            }

            int toAdd = EMS_EVENT_TARGET_COUNT - emsCount;
            Plugin.Log($"[EMS-Inject] Found {emsCount} EMS events in scene, injecting {toAdd} to reach {EMS_EVENT_TARGET_COUNT}");

            for (int i = 0; i < toAdd; i++)
            {
                // Create disabled so AddComponent doesn't trigger Awake() before we init fields
                var go = new GameObject($"EMS_Procedural_{emsCount + i + 1}");
                go.SetActive(false);
                go.transform.SetParent(__instance.transform);

                var ev = go.AddComponent<EmergencyEvent>();
                ev.eventName = "EMS Call";
                ev.eventType = "EMS";
                ev.show = true;

                // Initialize all array fields to empty — ActivateEvent iterates these
                // and will NRE if they're null (AddComponent leaves them as C# default null)
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
                Plugin.Log($"[EMS-Inject] Added {go.name} to emergency rotation (total events: {__instance.energencyEvents.Count})");
            }
        }

        // Fix 4: ReenableAllCalls - null check on missionbutton
        [HarmonyPatch(nameof(EmergencyManagerV2.ReenableAllCalls))]
        [HarmonyPrefix]
        public static bool ReenableAllCalls_NullSafe(EmergencyManagerV2 __instance)
        {
            for (int i = 0; i < __instance.energencyEvents.Count; i++)
            {
                if (__instance.energencyEvents[i].missionbutton != null)
                {
                    __instance.energencyEvents[i].missionbutton.SetActive(true);
                }
            }
            return false; // Skip original
        }

        // Fixes 1, 2, 3: Replace MainEmergencyLoop with corrected version
        [HarmonyPatch(nameof(EmergencyManagerV2.MainEmergencyLoop))]
        [HarmonyPostfix]
        public static void MainEmergencyLoop_LogFix(ref IEnumerator __result, EmergencyManagerV2 __instance)
        {
            // We wrap the original to patch the off-by-one and infinite loop issues.
            // Unfortunately we need to replace the coroutine entirely.
            __result = FixedMainEmergencyLoop(__instance);
        }

        private static IEnumerator FixedMainEmergencyLoop(EmergencyManagerV2 instance)
        {
            instance.looprunning = true;
            instance.resetLoop = false;

            var triggeredEmergencies = Traverse.Create(instance).Field("triggeredEmergencies")
                .GetValue<List<int>>();

            float initialWaitLoop = Random.Range(
                instance.startEventInterval.x, instance.startEventInterval.y) + Time.time;

            while (Time.time < initialWaitLoop && !instance.resetLoop)
            {
                yield return null;
            }

            while (instance.energencyEvents.Count > 0)
            {
                instance.CheckClearEv();
                instance.resetLoop = false;

                // Fix 2: Reset triggered list when all events have been used
                if (triggeredEmergencies.Count >= instance.energencyEvents.Count)
                {
                    triggeredEmergencies.Clear();
                }

                // Fix 1: Use energencyEvents.Count (not Count-1) for Random.Range upper bound
                // Random.Range(int,int) is exclusive on the upper bound, so Count is correct
                int emergencyIndex;
                EmergencyEvent emergencyEvent;
                int safetyCounter = 0;
                do
                {
                    emergencyIndex = Random.Range(0, instance.energencyEvents.Count);
                    safetyCounter++;
                    if (safetyCounter > 100)
                    {
                        // Fallback: clear and pick any
                        triggeredEmergencies.Clear();
                        emergencyIndex = Random.Range(0, instance.energencyEvents.Count);
                        break;
                    }
                }
                while (triggeredEmergencies.Contains(emergencyIndex));

                triggeredEmergencies.Add(emergencyIndex);
                emergencyEvent = instance.energencyEvents[emergencyIndex];

                if (emergencyEvent == null)
                    continue;

                // Handle procedural spawning (unchanged from original)
                if ((bool)instance.Procedural)
                {
                    SpawnProceduralEvent(instance, emergencyEvent);
                }

                instance.lastEvent.Add(emergencyEvent);

                // Fix 3: Only activate once (original activates 2-3 times!)
                emergencyEvent.ActivateEvent();
                instance.activeEvent = emergencyEvent;

                if (ConnectionManager.isMultiplayer)
                {
                    instance.photonView.RPC("OnStartEvent", Photon.Pun.RpcTarget.OthersBuffered, emergencyIndex);
                }

                while (instance.lastEvent.Count > 0)
                {
                    yield return null;
                }

                float waitTime = Random.Range(instance.intervalTillNewEvent.x, instance.intervalTillNewEvent.y);
                var timer = Traverse.Create(instance).Field("timer");
                timer.SetValue(Time.time + waitTime);
                while (timer.GetValue<float>() > Time.time && !instance.resetLoop)
                {
                    yield return null;
                }
            }
        }

        private static void SpawnProceduralEvent(EmergencyManagerV2 instance, EmergencyEvent ev)
        {
            string name = ev.name;
            if (name.Contains("Car Fire") || name.Contains("Van Fire") || name.Contains("RVFire"))
                instance.Procedural.StartCoroutine("SpawnCarFire");
            else if (name.Contains("GarageFire"))
                instance.Procedural.StartCoroutine("SpawnGarageFire");
            else if (name.Contains("BrownStoner"))
                instance.Procedural.StartCoroutine("BrownStoneFire");
            else if (name.Contains("2StoryVacant"))
                instance.Procedural.StartCoroutine("Vacant2Stry");
            else if (name.Contains("VacantCommercial"))
                instance.Procedural.StartCoroutine("VacantCommercial");
            else if (name.Contains("EMS"))
                instance.Procedural.StartCoroutine("SpawnEMSCall");
            else if (name.Contains("ApartmentFire"))
                instance.Procedural.StartCoroutine("ApartmentFire");
            else if (name.Contains("WhiteBrownStone"))
                instance.Procedural.StartCoroutine("WhiteBrownStone");
            else if (name.Contains("FoodstandFire"))
                instance.Procedural.StartCoroutine("SpawnFoodStandFire");
            else if (name.Contains("TrailerFire"))
                instance.Procedural.StartCoroutine("TrailerFire");
            else if (name.Contains("Vacant 2"))
                instance.Procedural.StartCoroutine("VacantFire2");
            else if (name.Contains("Manhole"))
                instance.Procedural.StartCoroutine("SpawnManholeFire");
            else if (name.Contains("DumpsterFire"))
                instance.Procedural.StartCoroutine("SpawnDumpsterFire");
            else if (name.Contains("LargeDumpster"))
                instance.Procedural.StartCoroutine("SpawnBigDumpsterFire");
            else if (name.Contains("NewsStandFire"))
                instance.Procedural.StartCoroutine("SpawnNewsStandFire");
            else if (name.Contains("TrashFir"))
            {
                // Fix off-by-one: original has gap at num==2 and num==4 (no fire spawns)
                int num = Random.Range(0, 4);
                switch (num)
                {
                    case 0: instance.Procedural.StartCoroutine("SpawnTrashBinFire"); break;
                    case 1: instance.Procedural.StartCoroutine("SpawnTrashBin2Fire"); break;
                    case 2: instance.Procedural.StartCoroutine("SpawnTrashPileFire"); break;
                    case 3: instance.Procedural.StartCoroutine("SpawnTrashPile2Fire"); break;
                }
            }
            else if (name.Contains("ScaffoldingFire"))
                instance.Procedural.StartCoroutine("SpawnScaffoldingFire");
            else if (name.Contains("Police"))
            {
                // Police events are handled by PoliceEnhancement plugin's PoliceDispatchManager
                // No native procedural spawner exists — just activate the event normally
            }
        }
    }
}
