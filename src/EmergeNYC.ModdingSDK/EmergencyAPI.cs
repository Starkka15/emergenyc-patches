using System;
using System.Collections.Generic;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Emergency event lifecycle hooks.
    /// Patch points: EmergencyManagerV2.ActivateScenario, ClearEmergencies, OnEndEvent, AutoRespond.
    /// </summary>
    public static class EmergencyAPI
    {
        // ── Events ──────────────────────────────────────────────────────────
        public static event Action<EmergencyEvent>? OnEmergencyStart;
        public static event Action<EmergencyEvent>? OnEmergencyEnd;

        /// <summary>Fired when a dispatch ticket is sent (address, assignment text).</summary>
        public static event Action<string, string>? OnCallDispatched;

        public static event Action? OnAutoRespond;

        // ── Queries (V15: read-only, always dereferences live singleton) ──
        public static EmergencyEvent? ActiveEmergency =>
            EmergencyManagerV2.instance?.activeEvent;

        public static List<EmergencyEvent>? AllEvents =>
            EmergencyManagerV2.instance?.energencyEvents;

        public static bool IsMultiplayer =>
            EmergencyManagerV2.instance?.isMultiplayer ?? false;

        // ── Bridge delegates (set by CommunityFixes on Init) ────────────────
        // (none needed — queries go direct to singleton)

        // ── Internal raise (called by SDKEmergencyBridge patches) ───────────
        internal static void RaiseEmergencyStart(EmergencyEvent ev) =>
            EmergeNYCSDK.SafeRaise(OnEmergencyStart, ev, nameof(OnEmergencyStart));

        internal static void RaiseEmergencyEnd(EmergencyEvent ev) =>
            EmergeNYCSDK.SafeRaise(OnEmergencyEnd, ev, nameof(OnEmergencyEnd));

        internal static void RaiseCallDispatched(string address, string assignment) =>
            EmergeNYCSDK.SafeRaise(OnCallDispatched, address, assignment, nameof(OnCallDispatched));

        internal static void RaiseAutoRespond() =>
            EmergeNYCSDK.SafeRaise(OnAutoRespond, nameof(OnAutoRespond));
    }
}
