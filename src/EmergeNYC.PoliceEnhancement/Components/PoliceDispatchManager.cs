using UnityEngine;

namespace EmergeNYC.PoliceEnhancement.Components
{
    /// <summary>
    /// Monitors active emergency events for police calls and handles
    /// police-specific scene setup (NPC placement, perimeter, etc.).
    /// Attached to the plugin GameObject in Plugin.Start().
    /// </summary>
    public class PoliceDispatchManager : MonoBehaviour
    {
        private EmergencyManagerV2? emergencyManager;
        private float checkInterval = 2f;
        private float lastCheck;
        private EmergencyEvent? currentPoliceEvent;

        private void Start()
        {
            Plugin.Log("[PoliceDispatch] Manager started, searching for EmergencyManagerV2...");
        }

        private void Update()
        {
            if (Time.time - lastCheck < checkInterval) return;
            lastCheck = Time.time;

            // Lazy-find the emergency manager
            if (emergencyManager == null)
            {
                emergencyManager = FindObjectOfType<EmergencyManagerV2>();
                if (emergencyManager == null) return;
                Plugin.Log("[PoliceDispatch] Found EmergencyManagerV2");
            }

            // Check for active police events
            if (emergencyManager.lastEvent == null || emergencyManager.lastEvent.Count == 0)
            {
                if (currentPoliceEvent != null)
                {
                    OnPoliceEventEnded();
                    currentPoliceEvent = null;
                }
                return;
            }

            // Look for police event in active events
            EmergencyEvent? policeEvent = null;
            for (int i = 0; i < emergencyManager.lastEvent.Count; i++)
            {
                var ev = emergencyManager.lastEvent[i];
                if (ev != null && ev.eventName != null && ev.eventName.Contains("Police"))
                {
                    policeEvent = ev;
                    break;
                }
            }

            if (policeEvent != null && policeEvent != currentPoliceEvent)
            {
                currentPoliceEvent = policeEvent;
                OnPoliceEventStarted(policeEvent);
            }
            else if (policeEvent == null && currentPoliceEvent != null)
            {
                OnPoliceEventEnded();
                currentPoliceEvent = null;
            }
        }

        private void OnPoliceEventStarted(EmergencyEvent ev)
        {
            Plugin.Log($"[PoliceDispatch] Police event active: {ev.eventName} at {ev.transform.position}");

            // Position perimeter officers around the scene
            var perimeter = GetComponent<PoliceScenePerimeter>();
            if (perimeter != null)
            {
                perimeter.SetupPerimeter(ev.transform.position, 15f);
            }
        }

        private void OnPoliceEventEnded()
        {
            Plugin.Log("[PoliceDispatch] Police event ended, cleaning up");

            var perimeter = GetComponent<PoliceScenePerimeter>();
            if (perimeter != null)
            {
                perimeter.ClearPerimeter();
            }
        }
    }
}
