using HarmonyLib;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.CommunityFixes.Patches
{
    // T11: Fire EMSAPI events from EMSPatient patch points.

    [HarmonyPatch(typeof(EMSPatient), "OnEnable")]
    public static class SDK_EMSPatient_OnEnable
    {
        [HarmonyPostfix]
        public static void Postfix(EMSPatient __instance) =>
            EMSAPI.RaisePatientSpawned(__instance);
    }

    [HarmonyPatch(typeof(EMSPatient), "Backboard")]
    public static class SDK_EMSPatient_Backboard
    {
        [HarmonyPostfix]
        public static void Postfix(EMSPatient __instance) =>
            EMSAPI.RaisePatientBackboarded(__instance);
    }

    [HarmonyPatch(typeof(EMSPatient), "RaiseLocalEvent")]
    public static class SDK_EMSPatient_RaiseLocalEvent
    {
        [HarmonyPostfix]
        public static void Postfix(EMSPatient __instance, string eventName) =>
            EMSAPI.RaiseTreatmentApplied(__instance, eventName);
    }
}
