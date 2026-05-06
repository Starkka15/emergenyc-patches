using System;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Convenience re-export of all SDK events in one place.
    /// External mods can subscribe here or directly on the individual API classes.
    /// </summary>
    public static class EventBus
    {
        // ── Emergency ────────────────────────────────────────────────────────
        public static event Action<EmergencyEvent>? OnEmergencyStart
        {
            add    => EmergencyAPI.OnEmergencyStart += value;
            remove => EmergencyAPI.OnEmergencyStart -= value;
        }
        public static event Action<EmergencyEvent>? OnEmergencyEnd
        {
            add    => EmergencyAPI.OnEmergencyEnd += value;
            remove => EmergencyAPI.OnEmergencyEnd -= value;
        }
        public static event Action<string, string>? OnCallDispatched
        {
            add    => EmergencyAPI.OnCallDispatched += value;
            remove => EmergencyAPI.OnCallDispatched -= value;
        }
        public static event Action? OnAutoRespond
        {
            add    => EmergencyAPI.OnAutoRespond += value;
            remove => EmergencyAPI.OnAutoRespond -= value;
        }

        // ── Fire ─────────────────────────────────────────────────────────────
        public static event Action<FireController>? OnFireIgnited
        {
            add    => FireAPI.OnFireIgnited += value;
            remove => FireAPI.OnFireIgnited -= value;
        }
        public static event Action<FireController>? OnFireExtinguished
        {
            add    => FireAPI.OnFireExtinguished += value;
            remove => FireAPI.OnFireExtinguished -= value;
        }
        public static event Action<FireController, float>? OnFireIntensityChanged
        {
            add    => FireAPI.OnFireIntensityChanged += value;
            remove => FireAPI.OnFireIntensityChanged -= value;
        }
        public static event Action<FireController, float, string>? OnWaterApplied
        {
            add    => FireAPI.OnWaterApplied += value;
            remove => FireAPI.OnWaterApplied -= value;
        }

        // ── EMS ──────────────────────────────────────────────────────────────
        public static event Action<EMSPatient>? OnPatientSpawned
        {
            add    => EMSAPI.OnPatientSpawned += value;
            remove => EMSAPI.OnPatientSpawned -= value;
        }
        public static event Action<EMSPatient>? OnPatientBackboarded
        {
            add    => EMSAPI.OnPatientBackboarded += value;
            remove => EMSAPI.OnPatientBackboarded -= value;
        }
        public static event Action<EMSPatient, string>? OnTreatmentApplied
        {
            add    => EMSAPI.OnTreatmentApplied += value;
            remove => EMSAPI.OnTreatmentApplied -= value;
        }

        // ── Traffic ──────────────────────────────────────────────────────────
        public static event Action<TSTrafficAI>? OnCarYieldStart
        {
            add    => TrafficAPI.OnCarYieldStart += value;
            remove => TrafficAPI.OnCarYieldStart -= value;
        }
        public static event Action<TSTrafficAI>? OnCarYieldEnd
        {
            add    => TrafficAPI.OnCarYieldEnd += value;
            remove => TrafficAPI.OnCarYieldEnd -= value;
        }
        public static event Action<TSTrafficAI>? OnCarSpawned
        {
            add    => TrafficAPI.OnCarSpawned += value;
            remove => TrafficAPI.OnCarSpawned -= value;
        }
        public static event Action<TSTrafficAI>? OnCarDespawned
        {
            add    => TrafficAPI.OnCarDespawned += value;
            remove => TrafficAPI.OnCarDespawned -= value;
        }

        // ── Characters ───────────────────────────────────────────────────────
        public static event Action<AIManager>? OnCharacterSpawned
        {
            add    => CharacterAPI.OnCharacterSpawned += value;
            remove => CharacterAPI.OnCharacterSpawned -= value;
        }
        public static event Action<AIManager>? OnAITakeControl
        {
            add    => CharacterAPI.OnAITakeControl += value;
            remove => CharacterAPI.OnAITakeControl -= value;
        }
        public static event Action<AIManager>? OnPlayerTakeControl
        {
            add    => CharacterAPI.OnPlayerTakeControl += value;
            remove => CharacterAPI.OnPlayerTakeControl -= value;
        }

        // ── Vehicles ─────────────────────────────────────────────────────────
        public static event Action<GameObject>? OnVehicleSpawned
        {
            add    => VehicleAPI.OnVehicleSpawned += value;
            remove => VehicleAPI.OnVehicleSpawned -= value;
        }
        public static event Action<FFD_SirenControl, FFD_SirenControl.SirenState>? OnSirenActivated
        {
            add    => VehicleAPI.OnSirenActivated += value;
            remove => VehicleAPI.OnSirenActivated -= value;
        }
        public static event Action<FFD_SirenControl>? OnSirenDeactivated
        {
            add    => VehicleAPI.OnSirenDeactivated += value;
            remove => VehicleAPI.OnSirenDeactivated -= value;
        }
        public static event Action<FFD_Airhorn>? OnAirhornUsed
        {
            add    => VehicleAPI.OnAirhornUsed += value;
            remove => VehicleAPI.OnAirhornUsed -= value;
        }
        public static event Action<NYPDSirenController>? OnPoliceSirenChanged
        {
            add    => VehicleAPI.OnPoliceSirenChanged += value;
            remove => VehicleAPI.OnPoliceSirenChanged -= value;
        }

        // ── Dispatch ─────────────────────────────────────────────────────────
        public static event Action<string, Vector3>? OnCallGenerated
        {
            add    => DispatchAPI.OnCallGenerated += value;
            remove => DispatchAPI.OnCallGenerated -= value;
        }
        public static event Action<string, Emergency>? OnUnitAssigned
        {
            add    => DispatchAPI.OnUnitAssigned += value;
            remove => DispatchAPI.OnUnitAssigned -= value;
        }
        public static event Action<string, string>? OnTicketSent
        {
            add    => DispatchAPI.OnTicketSent += value;
            remove => DispatchAPI.OnTicketSent -= value;
        }
    }
}
