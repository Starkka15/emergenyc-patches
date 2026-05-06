using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// EMS patient lifecycle hooks.
    /// Patch points: EMSPatient.OnEnable (spawned), Backboard (backboarded), RaiseLocalEvent (treatment).
    /// </summary>
    public static class EMSAPI
    {
        // ── Events ──────────────────────────────────────────────────────────
        public static event Action<EMSPatient>? OnPatientSpawned;
        public static event Action<EMSPatient>? OnPatientBackboarded;

        /// <summary>args: patient, eventName (treatment type string).</summary>
        public static event Action<EMSPatient, string>? OnTreatmentApplied;

        // ── Queries (V15) ────────────────────────────────────────────────────
        public static List<EMSPatient> AllPatients => EMSPatient.patients;

        // ── Internal raise ───────────────────────────────────────────────────
        internal static void RaisePatientSpawned(EMSPatient p) =>
            EmergeNYCSDK.SafeRaise(OnPatientSpawned, p, nameof(OnPatientSpawned));

        internal static void RaisePatientBackboarded(EMSPatient p) =>
            EmergeNYCSDK.SafeRaise(OnPatientBackboarded, p, nameof(OnPatientBackboarded));

        internal static void RaiseTreatmentApplied(EMSPatient p, string eventName) =>
            EmergeNYCSDK.SafeRaise(OnTreatmentApplied, p, eventName, nameof(OnTreatmentApplied));
    }
}
