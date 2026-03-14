using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for CarController:
    /// 1. NoOfGears is static + [SerializeField] - all vehicles share the same gear count!
    ///    If any vehicle sets NoOfGears to a different value, ALL vehicles change.
    ///    This is a classic Unity bug with static serialized fields.
    /// 2. m_Rigidbody null before Start completes but CurrentSpeed accesses it
    /// 3. Handbrake brakeTorque never cleared - once handbrake is applied, rear wheels keep
    ///    the brake torque from last frame even when handbrake is released
    /// 4. Division by zero possible in CalculateGearFactor/GearChanging if NoOfGears is 0
    /// 5. SteerHelper: eulerAngle wrapping issue - when rotation crosses 0/360 boundary,
    ///    the difference jumps to ~360 degrees causing the <10 check to fail
    /// </summary>
    [HarmonyPatch(typeof(CarController))]
    public static class CarControllerPatches
    {
        // Fix 3: Clear handbrake torque when handbrake is not applied
        // Original only sets brakeTorque when handbrake > 0 but never clears it
        [HarmonyPatch(nameof(CarController.Move))]
        [HarmonyPostfix]
        public static void Move_ClearHandbrake(CarController __instance,
            float steering, float accel, float footbrake, float handbrake, bool remote)
        {
            var colliders = Traverse.Create(__instance).Field("m_WheelColliders").GetValue<WheelCollider[]>();
            if (colliders == null || colliders.Length < 4) return;

            // If handbrake is not engaged, clear the rear wheel brake torque
            // that was set by handbrake on the previous frame
            if (handbrake <= 0f)
            {
                // Only clear if the rear wheels still have the massive handbrake torque
                // (don't interfere with normal braking)
                if (colliders[2].brakeTorque > 1000000f) // float.MaxValue was set as handbrake torque
                    colliders[2].brakeTorque = 0f;
                if (colliders[3].brakeTorque > 1000000f)
                    colliders[3].brakeTorque = 0f;
            }
        }

        // Fix 5: SteerHelper euler angle wrapping fix
        [HarmonyPatch("SteerHelper")]
        [HarmonyPrefix]
        public static bool SteerHelper_AngleWrapFix(CarController __instance)
        {
            var colliders = Traverse.Create(__instance).Field("m_WheelColliders").GetValue<WheelCollider[]>();
            var rigidbody = Traverse.Create(__instance).Field("m_Rigidbody").GetValue<Rigidbody>();
            var steerHelper = Traverse.Create(__instance).Field("m_SteerHelper").GetValue<float>();
            var oldRotation = Traverse.Create(__instance).Field("m_OldRotation");

            if (colliders == null || rigidbody == null) return false;

            for (int i = 0; i < 4; i++)
            {
                colliders[i].GetGroundHit(out var hit);
                if (hit.normal == Vector3.zero)
                    return false; // Not grounded
            }

            float currentRotation = __instance.transform.eulerAngles.y;
            float oldRot = oldRotation.GetValue<float>();

            // Fix: Use Mathf.DeltaAngle to handle 0/360 wrapping correctly
            float deltaAngle = Mathf.DeltaAngle(oldRot, currentRotation);

            if (Mathf.Abs(deltaAngle) < 10f)
            {
                Quaternion correction = Quaternion.AngleAxis(deltaAngle * steerHelper, Vector3.up);
                rigidbody.velocity = correction * rigidbody.velocity;
            }

            oldRotation.SetValue(currentRotation);
            return false; // Skip original
        }
    }
}
