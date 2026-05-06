using HarmonyLib;
using UnityEngine;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.CommunityFixes.Patches
{
    // T15: Fire DispatchAPI events from ProceduralEmergencySystem patch points.

    /// <summary>
    /// SendOutTicket — fires after the dispatch ticket is broadcast.
    /// Reads address and box from the PES instance (V15: read-only).
    /// Also fires OnCallGenerated with the emergency position.
    /// </summary>
    [HarmonyPatch(typeof(ProceduralEmergencySystem), "SendOutTicket")]
    public static class SDK_PES_SendOutTicket
    {
        [HarmonyPostfix]
        public static void Postfix(ProceduralEmergencySystem __instance)
        {
            string address = __instance.currentEmergencyAddress ?? string.Empty;
            string box     = __instance.currentEmergencyBox     ?? string.Empty;

            DispatchAPI.RaiseTicketSent(address, box);

            // Derive call type from ActiveEmergency buildingtype or use "Unknown"
            string callType = __instance.ActiveEmergency?.buildingtype ?? "Unknown";
            Vector3 pos = __instance.CurrentEmergencyPos != null
                ? __instance.CurrentEmergencyPos.position
                : Vector3.zero;

            DispatchAPI.RaiseCallGenerated(callType, pos);
            EmergencyAPI.RaiseCallDispatched(address, box);
        }
    }

    /// <summary>
    /// Patch FindNearestEngines to detect unit assignment.
    /// The method sets FirstDueEngine, SecondDueEngine, ThirdDueEngine.
    /// Fire OnUnitAssigned for each assigned unit postfix.
    /// </summary>
    [HarmonyPatch(typeof(ProceduralEmergencySystem), "FindNearestEngines")]
    public static class SDK_PES_FindNearestEngines
    {
        [HarmonyPostfix]
        public static void Postfix(ProceduralEmergencySystem __instance)
        {
            var ev = __instance.ActiveEmergency;
            if (ev == null) return;

            if (__instance.FirstDueEngine != null)
                DispatchAPI.RaiseUnitAssigned(__instance.FirstDueEngine.name, ev);
            if (__instance.SecondDueEngine != null)
                DispatchAPI.RaiseUnitAssigned(__instance.SecondDueEngine.name, ev);
            if (__instance.ThirdDueEngine != null)
                DispatchAPI.RaiseUnitAssigned(__instance.ThirdDueEngine.name, ev);
        }
    }

    [HarmonyPatch(typeof(ProceduralEmergencySystem), "FindNearestTrucks")]
    public static class SDK_PES_FindNearestTrucks
    {
        [HarmonyPostfix]
        public static void Postfix(ProceduralEmergencySystem __instance)
        {
            var ev = __instance.ActiveEmergency;
            if (ev == null) return;

            if (__instance.FirstDueTruck != null)
                DispatchAPI.RaiseUnitAssigned(__instance.FirstDueTruck.name, ev);
            if (__instance.SecondDueTruck != null)
                DispatchAPI.RaiseUnitAssigned(__instance.SecondDueTruck.name, ev);
        }
    }
}
