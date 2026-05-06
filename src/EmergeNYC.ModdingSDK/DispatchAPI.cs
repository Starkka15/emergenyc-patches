using System;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Dispatch and call generation hooks.
    /// Patch points: ProceduralEmergencySystem.SendOutTicket, GetAddress.
    /// Bridge delegates wired by CommunityFixes at init.
    /// </summary>
    public static class DispatchAPI
    {
        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fired when a new call is generated. args: call type string, world position.</summary>
        public static event Action<string, Vector3>? OnCallGenerated;

        /// <summary>Fired when a unit is assigned to an emergency. args: unit name, emergency scene object.</summary>
        public static event Action<string, Emergency>? OnUnitAssigned;

        /// <summary>Fired when a dispatch ticket is broadcast. args: address, box number.</summary>
        public static event Action<string, string>? OnTicketSent;

        // ── Bridge delegates ─────────────────────────────────────────────────
        public static Action<string>? ForceEmergencyImpl;
        public static Func<Vector3, Transform?>? GetNearestEngineImpl;
        public static Func<Vector3, Transform?>? GetNearestTruckImpl;

        // ── Public helpers ───────────────────────────────────────────────────

        /// <summary>Force a procedural emergency of the given type (e.g. "GarageFire", "EMSCall").</summary>
        public static void ForceEmergency(string type) =>
            ForceEmergencyImpl?.Invoke(type);

        /// <summary>Returns the nearest engine station Transform to a world position.</summary>
        public static Transform? GetNearestEngine(Vector3 position) =>
            GetNearestEngineImpl?.Invoke(position);

        /// <summary>Returns the nearest ladder/truck station Transform to a world position.</summary>
        public static Transform? GetNearestTruck(Vector3 position) =>
            GetNearestTruckImpl?.Invoke(position);

        // ── Internal raise ───────────────────────────────────────────────────
        internal static void RaiseCallGenerated(string type, Vector3 pos) =>
            EmergeNYCSDK.SafeRaise(OnCallGenerated, type, pos, nameof(OnCallGenerated));

        internal static void RaiseUnitAssigned(string unit, Emergency ev) =>
            EmergeNYCSDK.SafeRaise(OnUnitAssigned, unit, ev, nameof(OnUnitAssigned));

        internal static void RaiseTicketSent(string address, string box) =>
            EmergeNYCSDK.SafeRaise(OnTicketSent, address, box, nameof(OnTicketSent));
    }
}
