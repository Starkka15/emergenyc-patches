using HarmonyLib;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.CommunityFixes.Patches
{
    // T9: Fire EmergencyAPI events from EmergencyManagerV2 patch points.

    [HarmonyPatch(typeof(EmergencyManagerV2), "ActivateScenario")]
    public static class SDK_EM2_ActivateScenario
    {
        [HarmonyPostfix]
        public static void Postfix(EmergencyManagerV2 __instance, EmergencyEvent ev)
        {
            EmergencyAPI.RaiseEmergencyStart(ev);
        }
    }

    [HarmonyPatch(typeof(EmergencyManagerV2), "ClearEmergencies")]
    public static class SDK_EM2_ClearEmergencies
    {
        // Capture the active event before it is cleared
        private static EmergencyEvent? _captured;

        [HarmonyPrefix]
        public static void Prefix(EmergencyManagerV2 __instance)
        {
            _captured = __instance.activeEvent;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (_captured != null)
                EmergencyAPI.RaiseEmergencyEnd(_captured);
            _captured = null;
        }
    }

    [HarmonyPatch(typeof(EmergencyManagerV2), "OnEndEvent")]
    public static class SDK_EM2_OnEndEvent
    {
        private static EmergencyEvent? _captured;

        [HarmonyPrefix]
        public static void Prefix(EmergencyManagerV2 __instance)
        {
            _captured = __instance.activeEvent;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (_captured != null)
                EmergencyAPI.RaiseEmergencyEnd(_captured);
            _captured = null;
        }
    }

    [HarmonyPatch(typeof(EmergencyManagerV2), "AutoRespond")]
    public static class SDK_EM2_AutoRespond
    {
        [HarmonyPostfix]
        public static void Postfix() => EmergencyAPI.RaiseAutoRespond();
    }
}
