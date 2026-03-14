using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for FireController.FireParticleAnimator:
    /// CRITICAL: UpdateSystem() has inverted cache logic repeated 4 times.
    /// The code checks "if (cachedEmissionRate != -1f)" then overwrites the cache,
    /// and in the else (when cache IS -1/uninitialized) tries to restore from it.
    /// This is backwards: when cache is -1 (first run), it should initialize the cache;
    /// when cache is valid (!= -1), it should restore from cache before recalculating.
    /// Additionally, the else branch sets rateOverDistanceMultiplier instead of rateOverTimeMultiplier.
    ///
    /// Result: Fire emission rates are never properly cached/restored between frames,
    /// causing fire extinguishing to not work correctly.
    ///
    /// Also fixes GetState/GetBaseState division by zero when cacheStartMultiplier is 0.
    /// </summary>
    [HarmonyPatch(typeof(FireController.FireParticleAnimator))]
    public static class FireParticleAnimatorPatches
    {
        [HarmonyPatch(nameof(FireController.FireParticleAnimator.UpdateSystem))]
        [HarmonyPrefix]
        public static bool UpdateSystem_FixCacheLogic(FireController.FireParticleAnimator __instance)
        {
            if (!__instance.isBehaviourActive)
                return false;

            var traverse = Traverse.Create(__instance);
            var fireBehavioursOptimized = traverse.Field("fireBehavioursOptimized")
                .GetValue<Dictionary<string, FireController.FireBehaviour>>();
            var cachedEmissionRateField = traverse.Field("cachedEmissionRate");
            var cacheStartMultiplierField = traverse.Field("cacheStartMultiplier");
            var cacheStartMultiplierEMGField = traverse.Field("cacheStartMultiplierEMG");

            float cachedEmissionRate = cachedEmissionRateField.GetValue<float>();

            foreach (var ps in __instance.particleSystems)
            {
                if (ps == null || __instance.growthRateSystemRate == null)
                    continue;

                var em = ps.emission;
                var emg = __instance.growthRateSystemRate.emission;

                // Initialize dictionaries on first run
                if (fireBehavioursOptimized == null)
                {
                    fireBehavioursOptimized = new Dictionary<string, FireController.FireBehaviour>();
                    foreach (var fb in __instance.systemSubstanceInteractions)
                    {
                        if (fb != null && fb.behaviourName != null &&
                            !fireBehavioursOptimized.ContainsKey(fb.behaviourName))
                        {
                            fireBehavioursOptimized.Add(fb.behaviourName, fb);
                        }
                    }
                    traverse.Field("fireBehavioursOptimized").SetValue(fireBehavioursOptimized);
                    cacheStartMultiplierField.SetValue(em.rateOverTimeMultiplier);
                    cacheStartMultiplierEMGField.SetValue(emg.rateOverTimeMultiplier);
                }

                float cacheStartMultiplier = cacheStartMultiplierField.GetValue<float>();
                float cacheStartMultiplierEMG = cacheStartMultiplierEMGField.GetValue<float>();

                if (__instance.systemGrowth == null || !__instance.systemGrowth.hasEmissionAnimation)
                    continue;

                // FIX: Inverted cache logic. Original had != -1f (cache valid) -> overwrite cache
                // and == -1f (cache invalid) -> restore from invalid cache. This is backwards.
                if (cachedEmissionRate == -1f)
                {
                    // First run: initialize cache from current emission rate
                    cachedEmissionRate = em.rateOverTimeMultiplier;
                }
                else
                {
                    // Subsequent runs: restore emission rate from cache before recalculating
                    // FIX: Original used rateOverDistanceMultiplier instead of rateOverTimeMultiplier
                    em.rateOverTimeMultiplier = cachedEmissionRate;
                }

                float growthRatio = (cacheStartMultiplierEMG > 0f)
                    ? emg.rateOverTimeMultiplier / cacheStartMultiplierEMG
                    : 0f;

                if (float.IsNaN(growthRatio))
                    growthRatio = 0f;
                growthRatio = Mathf.Clamp(growthRatio, 0f, 100f);

                if (__instance.curveType == FireController.FireParticleAnimator.CurveType.Additive)
                {
                    float growthDelta = cacheStartMultiplier *
                        (__instance.systemGrowth.emissionAnimation.Evaluate(growthRatio * 100f) / 100f);
                    em.rateOverTimeMultiplier += growthDelta * Time.deltaTime;
                }
                else // IndexProcentage
                {
                    float rate = cacheStartMultiplier *
                        __instance.systemGrowth.emissionAnimation.Evaluate(growthRatio);
                    em.rateOverTimeMultiplier = rate;
                }

                cachedEmissionRate = em.rateOverTimeMultiplier;
                em.rateOverTimeMultiplier *= __instance.baseMultiplier;
                em.rateOverTimeMultiplier = Mathf.Clamp(em.rateOverTimeMultiplier, 0f, cacheStartMultiplier);
            }

            cachedEmissionRateField.SetValue(cachedEmissionRate);

            // Check for fire extinction
            if (__instance.coreFire && __instance.particleSystems.Count > 0 &&
                __instance.particleSystems[0] != null)
            {
                var em = __instance.particleSystems[0].emission;
                if (em.rateOverTimeMultiplier == 0f)
                {
                    __instance.isBehaviourActive = false;
                }
            }

            return false; // Skip original
        }

        // Fix: GetState divides by cacheStartMultiplier which can be 0
        [HarmonyPatch(nameof(FireController.FireParticleAnimator.GetState))]
        [HarmonyPrefix]
        public static bool GetState_DivByZeroSafe(FireController.FireParticleAnimator __instance, ref float __result)
        {
            var traverse = Traverse.Create(__instance);
            if (!traverse.Field("p_isBehaviourActive").GetValue<bool>())
            {
                __result = 0f;
                return false;
            }

            __instance.SanityCheck();

            if (__instance.particleSystems.Count > 0 && __instance.particleSystems[0] != null)
            {
                var em = __instance.particleSystems[0].emission;
                float startMult = traverse.Field("cacheStartMultiplier").GetValue<float>();
                __result = (startMult > 0f) ? em.rateOverTimeMultiplier / startMult : 0f;
            }
            else
            {
                __result = 0f;
            }
            return false;
        }

        // Fix: GetBaseState divides by cacheStartMultiplier which can be 0
        [HarmonyPatch(nameof(FireController.FireParticleAnimator.GetBaseState))]
        [HarmonyPrefix]
        public static bool GetBaseState_DivByZeroSafe(FireController.FireParticleAnimator __instance, ref float __result)
        {
            if (__instance.particleSystems.Count > 0 && __instance.particleSystems[0] != null)
            {
                var em = __instance.particleSystems[0].emission;
                float startMult = Traverse.Create(__instance).Field("cacheStartMultiplier").GetValue<float>();
                __result = (startMult > 0f) ? em.rateOverTimeMultiplier / startMult : 0f;
            }
            else
            {
                __result = 0f;
            }
            return false;
        }
    }
}
