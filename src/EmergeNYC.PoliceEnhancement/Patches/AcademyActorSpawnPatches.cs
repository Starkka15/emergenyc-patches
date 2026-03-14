using HarmonyLib;

namespace EmergeNYC.PoliceEnhancement.Patches
{
    /// <summary>
    /// Fixes a bug in AcademyActorSpawn.Start where EMS spawn areas
    /// are incorrectly assigned AcademySpawnAreaType.NYPD instead of EMS.
    /// </summary>
    [HarmonyPatch(typeof(AcademyActorSpawn), "Start")]
    public static class AcademyActorSpawnStartPatch
    {
        public static void Postfix(AcademyActorSpawn __instance)
        {
            // Fix: the native code checks name.Contains("EMS") but sets spawnAreaType = NYPD
            if (__instance.gameObject.name.Contains("EMS") &&
                __instance.loaderSetup != null &&
                __instance.loaderSetup.spawnAreaType == AcademySpawnAreaType.NYPD)
            {
                __instance.loaderSetup.spawnAreaType = AcademySpawnAreaType.EMS;
                Plugin.Log($"[SpawnFix] Corrected {__instance.gameObject.name} from NYPD → EMS");
            }
        }
    }
}
