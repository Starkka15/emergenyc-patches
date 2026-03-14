using HarmonyLib;
using EmergeNYC.EMSEnhancement.Components;

namespace EmergeNYC.EMSEnhancement.Patches
{
    /// <summary>
    /// Harmony patches that wire vital sign deterioration into the native EMS system.
    ///
    /// The native system has vital components (VascularSystem, RespiratorySystem, etc.)
    /// but they're static. We attach EMSPatientEnhancer to make vitals deteriorate
    /// based on the patient's condition. The native treatment UI (H key) counteracts
    /// the deterioration through EMSStatModifier.
    /// </summary>

    // Attach EMSPatientEnhancer when a patient's SituationManager initializes
    [HarmonyPatch(typeof(SituationManager), "Start")]
    public static class SituationManagerStartPatch
    {
        public static void Postfix(SituationManager __instance)
        {
            if (!Photon.Pun.PhotonNetwork.IsMasterClient)
                return;

            if (__instance.GetComponent<EMSPatientEnhancer>() != null)
                return;

            // Only attach if this patient has an EMS condition
            if (!__instance.cardiac && !__instance.gsw && !__instance.overdose &&
                !__instance.respiratory && !__instance.fall && !__instance.caraccident)
            {
                return;
            }

            var enhancer = __instance.gameObject.AddComponent<EMSPatientEnhancer>();
            enhancer.Initialize();
        }
    }

    // Log patient registration for diagnostics
    [HarmonyPatch(typeof(EMSPatient), "Awake")]
    public static class EMSPatientAwakePatch
    {
        public static void Postfix(EMSPatient __instance)
        {
            Plugin.Log($"[EMS] Patient registered: {__instance.gameObject.name} (total: {EMSPatient.patients.Count})");
        }
    }
}
