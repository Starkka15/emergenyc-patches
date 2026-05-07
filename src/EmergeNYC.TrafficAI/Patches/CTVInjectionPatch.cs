using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.TrafficAI.Patches
{
    /// <summary>
    /// T23: Inject CustomTrafficVehicle onto every TSTrafficAI vehicle on enable.
    /// CTV's own OnDisable handles TSTrafficAI restoration (V18).
    /// </summary>
    [HarmonyPatch(typeof(TSTrafficAI), "OnEnable")]
    public static class CTVInjectionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TSTrafficAI __instance)
        {
            var go = __instance.gameObject;
            if (go.GetComponent<CustomTrafficVehicle>() == null)
                go.AddComponent<CustomTrafficVehicle>();
        }
    }
}
