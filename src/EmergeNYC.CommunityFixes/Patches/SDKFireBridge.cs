using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.CommunityFixes.Patches
{
    // T10: Fire FireAPI events from FireController patch points.

    /// <summary>
    /// Awake = subscribe to per-instance fireParticles.OnExinctFire and fire OnFireIgnited.
    /// FireController has no OnEnable; Awake is called once per instance lifetime.
    /// OnExinctFire lives on FireParticleAnimator (inner class), not FireController directly.
    /// </summary>
    [HarmonyPatch(typeof(FireController), "Awake")]
    public static class SDK_FireController_Awake
    {
        [HarmonyPostfix]
        public static void Postfix(FireController __instance)
        {
            FireAPI.RaiseFireIgnited(__instance);

            // fireParticles.OnExinctFire is the inner-class event that fires on extinction
            __instance.fireParticles.OnExinctFire += () =>
                FireAPI.RaiseFireExtinguished(__instance);
        }
    }

    [HarmonyPatch(typeof(FireController), "ApplySubstanceOnSystem")]
    public static class SDK_FireController_ApplySubstance
    {
        [HarmonyPostfix]
        public static void Postfix(FireController __instance, string substanceName, float quantity)
        {
            FireAPI.RaiseWaterApplied(__instance, quantity, substanceName);
        }
    }

    /// <summary>
    /// Track intensity changes in Updates(). Only fire event when delta ≥ 0.05 to avoid spam.
    /// V14: read-only in postfix — no game method calls.
    /// </summary>
    [HarmonyPatch(typeof(FireController), "Updates")]
    public static class SDK_FireController_Updates
    {
        private static readonly Dictionary<int, float> _lastIntensity = new Dictionary<int, float>();
        private const float IntensityDeltaThreshold = 0.05f;

        [HarmonyPostfix]
        public static void Postfix(FireController __instance)
        {
            int id = __instance.GetInstanceID();
            float cur = __instance.fireIntensity;

            if (!_lastIntensity.TryGetValue(id, out float prev))
            {
                _lastIntensity[id] = cur;
                return;
            }

            if (Mathf.Abs(cur - prev) >= IntensityDeltaThreshold)
            {
                _lastIntensity[id] = cur;
                FireAPI.RaiseIntensityChanged(__instance, cur);
            }
        }
    }
}
