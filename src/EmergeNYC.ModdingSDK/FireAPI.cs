using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Fire system lifecycle hooks.
    /// Patch points: FireController.OnEnable (ignited), OnExinctFire Action (extinguished),
    /// ApplySubstanceOnSystem (water applied), Updates() with intensity delta.
    /// </summary>
    public static class FireAPI
    {
        // ── Events ──────────────────────────────────────────────────────────
        public static event Action<FireController>? OnFireIgnited;
        public static event Action<FireController>? OnFireExtinguished;

        /// <summary>Fired when fire intensity changes by ≥0.05 units.</summary>
        public static event Action<FireController, float>? OnFireIntensityChanged;

        /// <summary>Fired when a substance (water, retardant) is applied. args: controller, quantity, substanceName.</summary>
        public static event Action<FireController, float, string>? OnWaterApplied;

        // ── Helpers ─────────────────────────────────────────────────────────
        public static FireController[] GetAllFires() =>
            UnityEngine.Object.FindObjectsOfType<FireController>();

        // ── Internal raise ───────────────────────────────────────────────────
        internal static void RaiseFireIgnited(FireController fc) =>
            EmergeNYCSDK.SafeRaise(OnFireIgnited, fc, nameof(OnFireIgnited));

        internal static void RaiseFireExtinguished(FireController fc) =>
            EmergeNYCSDK.SafeRaise(OnFireExtinguished, fc, nameof(OnFireExtinguished));

        internal static void RaiseIntensityChanged(FireController fc, float intensity) =>
            EmergeNYCSDK.SafeRaise(OnFireIntensityChanged, fc, intensity, nameof(OnFireIntensityChanged));

        internal static void RaiseWaterApplied(FireController fc, float quantity, string substance) =>
            EmergeNYCSDK.SafeRaise(OnWaterApplied, fc, quantity, substance, nameof(OnWaterApplied));
    }
}
