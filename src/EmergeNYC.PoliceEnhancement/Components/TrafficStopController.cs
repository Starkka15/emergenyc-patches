using UnityEngine;
using EmergeNYC.PoliceEnhancement.Patches;

namespace EmergeNYC.PoliceEnhancement.Components
{
    /// <summary>
    /// Traffic stop state machine. When the player is in an NYPD vehicle,
    /// they can initiate a traffic stop on a nearby civilian car.
    ///
    /// Flow:
    /// 1. Player drives NYPD vehicle near a TrafficCarBaked
    /// 2. Press F5 to initiate stop — car pulls over (FreeLane + speed zero)
    /// 3. IMGUI panel shows interaction options
    /// 4. Press F5 again to release the car
    ///
    /// Attached to the plugin GameObject in Plugin.Start().
    /// </summary>
    public class TrafficStopController : MonoBehaviour
    {
        private enum StopState
        {
            Idle,
            Stopping,
            Stopped,
            Releasing
        }

        private StopState state = StopState.Idle;
        private TrafficCarBaked? targetCar;
        private float stopTimer;
        private bool showUI;

        // Detection settings
        private const float DetectionRange = 20f;
        private const KeyCode StopKey = KeyCode.F5;
        private const float StopSettleTime = 2f;

        private void Update()
        {
            // Only process when F5 is pressed
            if (Input.GetKeyDown(StopKey))
            {
                switch (state)
                {
                    case StopState.Idle:
                        TryInitiateStop();
                        break;
                    case StopState.Stopped:
                        ReleaseStop();
                        break;
                }
            }

            // Handle stopping transition
            if (state == StopState.Stopping)
            {
                stopTimer += Time.deltaTime;
                if (stopTimer >= StopSettleTime)
                {
                    state = StopState.Stopped;
                    showUI = true;
                    Plugin.Log("[TrafficStop] Car has stopped, showing interaction UI");
                }
            }
        }

        private void TryInitiateStop()
        {
            // Check if player is in an NYPD vehicle
            if (!IsPlayerInPoliceVehicle())
                return;

            // Find nearest traffic car in range
            var player = Camera.main;
            if (player == null) return;

            Vector3 playerPos = player.transform.position;
            TrafficCarBaked? nearest = null;
            float nearestDist = DetectionRange;

            var cars = FindObjectsOfType<TrafficCarBaked>();
            for (int i = 0; i < cars.Length; i++)
            {
                // Skip already-stopped cars
                if (TrafficCarBakedUpdatePatch.StoppedCars.Contains(cars[i].GetInstanceID()))
                    continue;

                float dist = Vector3.Distance(playerPos, cars[i].transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = cars[i];
                }
            }

            if (nearest == null)
            {
                Plugin.Log("[TrafficStop] No civilian vehicle in range");
                return;
            }

            targetCar = nearest;
            TrafficCarBakedUpdatePatch.StopCar(targetCar);
            state = StopState.Stopping;
            stopTimer = 0f;
            Plugin.Log($"[TrafficStop] Initiating stop on {targetCar.gameObject.name} (dist: {nearestDist:F1}m)");
        }

        private void ReleaseStop()
        {
            if (targetCar != null)
            {
                TrafficCarBakedUpdatePatch.ReleaseCar(targetCar);
                Plugin.Log($"[TrafficStop] Released {targetCar.gameObject.name}");
            }

            targetCar = null;
            state = StopState.Idle;
            showUI = false;
        }

        private bool IsPlayerInPoliceVehicle()
        {
            // NYPDSirenController.IsPlayerIn tracks whether the player is in a police car
            var sirens = FindObjectsOfType<NYPDSirenController>();
            for (int i = 0; i < sirens.Length; i++)
            {
                if (sirens[i].IsPlayerIn)
                    return true;
            }
            return false;
        }

        private void OnGUI()
        {
            if (!showUI || targetCar == null) return;

            float w = 280f;
            float h = 140f;
            float x = Screen.width - w - 20f;
            float y = Screen.height / 2f - h / 2f;

            GUI.Box(new Rect(x, y, w, h), "Traffic Stop");

            GUI.Label(new Rect(x + 10, y + 25, w - 20, 25),
                $"Vehicle: {targetCar.gameObject.name}");

            GUI.Label(new Rect(x + 10, y + 50, w - 20, 25),
                $"Status: {state}");

            if (GUI.Button(new Rect(x + 10, y + 80, w - 20, 25), "Issue Warning"))
            {
                Plugin.Log($"[TrafficStop] Warning issued to {targetCar.gameObject.name}");
                ReleaseStop();
            }

            if (GUI.Button(new Rect(x + 10, y + 110, w - 20, 25), "Release Vehicle [F5]"))
            {
                ReleaseStop();
            }
        }

        private void OnDestroy()
        {
            // Release any stopped car on cleanup
            if (targetCar != null)
                ReleaseStop();
        }
    }
}
