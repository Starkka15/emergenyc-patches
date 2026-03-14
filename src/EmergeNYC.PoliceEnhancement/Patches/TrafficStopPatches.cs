using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.PoliceEnhancement.Patches
{
    /// <summary>
    /// Patches TrafficCarBaked.Update to support traffic stops.
    /// When a car's instance ID is in the stopped set, we zero its speed
    /// and lock it in place. TrafficStopController manages the set.
    /// </summary>
    [HarmonyPatch(typeof(TrafficCarBaked), "Update")]
    public static class TrafficCarBakedUpdatePatch
    {
        // Cars currently being stopped by the player
        internal static readonly HashSet<int> StoppedCars = new HashSet<int>();

        // FieldRef for the private currentSpeed field
        private static readonly AccessTools.FieldRef<TrafficCarBaked, float> currentSpeedRef =
            AccessTools.FieldRefAccess<TrafficCarBaked, float>("currentSpeed");

        public static void Postfix(TrafficCarBaked __instance)
        {
            if (StoppedCars.Count == 0) return;

            int id = __instance.GetInstanceID();
            if (!StoppedCars.Contains(id)) return;

            // Zero out speed to keep the car stationary
            currentSpeedRef(__instance) = 0f;
        }

        public static void StopCar(TrafficCarBaked car)
        {
            int id = car.GetInstanceID();
            if (StoppedCars.Add(id))
            {
                // Trigger lane change to pull over, then lock
                car.FreeLane();
                Plugin.Log($"[TrafficStop] Stopped car {car.gameObject.name} (ID:{id})");
            }
        }

        public static void ReleaseCar(TrafficCarBaked car)
        {
            int id = car.GetInstanceID();
            if (StoppedCars.Remove(id))
            {
                Plugin.Log($"[TrafficStop] Released car {car.gameObject.name} (ID:{id})");
            }
        }
    }
}
