using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Miscellaneous gameplay fixes:
    /// 1. AIHealthMonitor.Update: null chain when health is null - calls GetComponentInParent
    ///    every frame which can NRE if AITaskUIManager or AssignedAI is also null
    /// 2. FireApplyDamage.Update: string.Contains checks on gameObject name every frame
    ///    to set a constant value - should be done once in Start
    /// 3. FireDynamicsController.ApplyDamageMultiplier: uses float.Parse on damageObj.name
    ///    (stores damage as a GameObject name string!) - can throw FormatException
    /// 4. Singleton: FindObjectOfType called twice in getter when multiple instances exist
    /// </summary>
    public static class MiscGameplayPatches
    {
        // Fix 1: AIHealthMonitor - prevent NRE cascade when health ref is lost
        [HarmonyPatch(typeof(AIHealthMonitor), "Update")]
        public static class AIHealthMonitorUpdatePatch
        {
            public static bool Prefix(AIHealthMonitor __instance)
            {
                if (__instance.health == null)
                {
                    // Try to find health component safely instead of the fragile chain
                    var taskUI = __instance.GetComponentInParent<AITaskUIManager>();
                    if (taskUI != null && taskUI.AssignedAI != null)
                    {
                        __instance.health = taskUI.AssignedAI.GetComponent<Opsive.ThirdPersonController.CharacterHealth>();
                    }

                    // If still null, skip this frame
                    if (__instance.health == null)
                        return false;
                }

                var slider = __instance.GetComponent<Slider>();
                if (slider != null)
                {
                    slider.value = __instance.health.m_CurrentHealth;
                }

                return false; // Skip original
            }
        }

        // Fix 2: FireApplyDamage - set baseDamage once in Start instead of every frame
        [HarmonyPatch(typeof(FireApplyDamage), "Update")]
        public static class FireApplyDamageUpdatePatch
        {
            public static bool Prefix()
            {
                return false; // Skip the entire Update - it needlessly recalculates a constant
            }
        }

        [HarmonyPatch(typeof(FireApplyDamage), "Awake")]
        public static class FireApplyDamageAwakePatch
        {
            public static void Postfix(FireApplyDamage __instance)
            {
                // Do the name check once at startup instead of every frame
                string name = __instance.name;
                if (name.Contains("Shorts") || name.Contains("Sweater") || name.Contains("Shirt"))
                {
                    __instance.baseDamage = 15f;
                }
                else
                {
                    __instance.baseDamage = 3f;
                }
            }
        }

        // Fix 3: FireDynamicsController.ApplyDamageMultiplier - safe float parse
        [HarmonyPatch(typeof(FireDynamicsController), nameof(FireDynamicsController.ApplyDamageMultiplier))]
        public static class FireDamageMultiplierPatch
        {
            public static bool Prefix(FireDynamicsController __instance,
                Vector3 applyPoint, AnimationCurve distanceModifier, float baseMultiplier)
            {
                if (__instance.gridIgnition == null) return false;

                foreach (var item in __instance.gridIgnition)
                {
                    if (item == null || item.damageObj == null) continue;

                    float time = Vector3.Distance(applyPoint, item.transform.position);
                    float modifier = distanceModifier.Evaluate(time);

                    // Safe parse instead of float.Parse which can throw FormatException
                    if (!float.TryParse(item.damageObj.name, out float damageValue))
                    {
                        damageValue = 0f;
                    }

                    damageValue += baseMultiplier * modifier;
                    damageValue = Mathf.Clamp01(damageValue);
                    item.damageObj.name = damageValue.ToString("F4");
                }

                return false; // Skip original
            }
        }
    }
}
