using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for OutriggerManager:
    /// 1. CRITICAL: RigidbodyConstraints overwrite bug in Update - each constraint assignment
    ///    OVERWRITES the previous one instead of OR-combining them. Only the last constraint
    ///    (FreezeRotationY) actually takes effect. The developer clearly intended to combine
    ///    FreezePositionX | FreezePositionZ | FreezeRotationY. This causes the tower ladder
    ///    vehicle to slide around during outrigger deployment because X/Z aren't frozen.
    ///    Occurs at two separate locations in Update (Extending/Contracting and StillDone/Extending).
    /// 2. Excessive GetComponent/GetComponentInParent calls every frame in Update - the Rigidbody
    ///    and RCC_CarControllerV3 references are fetched multiple times per frame via GetComponent
    ///    instead of being cached.
    /// </summary>
    [HarmonyPatch(typeof(OutriggerManager))]
    public static class OutriggerManagerPatches
    {
        // The correct combined constraints the developer intended
        private static readonly RigidbodyConstraints CombinedFreezeConstraints =
            RigidbodyConstraints.FreezePositionX |
            RigidbodyConstraints.FreezePositionZ |
            RigidbodyConstraints.FreezeRotationY;

        // Replace the entire Update to fix both the constraints bug and the per-frame GetComponent spam.
        // All fields accessed here are public on OutriggerManager, so no Traverse needed.
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool Update_FixConstraintsAndCaching(OutriggerManager __instance)
        {
            // Cache component references once instead of calling GetComponent every frame
            Rigidbody rccexRb = __instance.rccex != null
                ? __instance.rccex.GetComponent<Rigidbody>()
                : null;
            RCC_CarControllerV3 carController =
                __instance.GetComponentInParent<RCC_CarControllerV3>();

            // Lazy-init physicsLockCOntroller (original logic, lines 153-156)
            if (__instance.physicsLockCOntroller == null
                && __instance.rccex != null
                && (bool)__instance.rccex.GetComponent<PhysicsLockController>())
            {
                __instance.physicsLockCOntroller = carController != null
                    ? carController.GetComponent<PhysicsLockController>()
                    : null;
            }

            // --- State: Extending or Contracting (lines 157-172) ---
            // FIX #1a: OR-combine constraints instead of overwriting
            if (__instance.state == OutriggerManager.State.Extending
                || __instance.state == OutriggerManager.State.Contracting)
            {
                if (rccexRb != null)
                {
                    rccexRb.constraints = CombinedFreezeConstraints;
                }
                __instance.rccex.outriggersgoingdown = true;

                if (carController != null)
                {
                    var allWheelColliders = Traverse.Create(carController)
                        .Field("allWheelColliders").GetValue<RCC_WheelCollider[]>();
                    if (allWheelColliders != null)
                    {
                        foreach (var wheelCollider in allWheelColliders)
                        {
                            if (wheelCollider != null && !wheelCollider.gameObject.activeSelf)
                            {
                                wheelCollider.gameObject.SetActive(true);
                            }
                        }
                    }
                }
            }

            // --- State: StillDone or Extending (lines 173-187) ---
            // FIX #1b: OR-combine constraints instead of overwriting
            if (__instance.state == OutriggerManager.State.StillDone
                || __instance.state == OutriggerManager.State.Extending)
            {
                if (rccexRb != null)
                {
                    rccexRb.constraints = CombinedFreezeConstraints;
                    rccexRb.angularDrag = 99999f;
                    rccexRb.drag = 99999f;
                }
                __instance.rccex.outriggersgoingdown = true;

                if (__instance.physicsLockCOntroller != null)
                {
                    __instance.physicsLockCOntroller.enabled = false;
                }

                if (carController != null)
                {
                    carController.startcheck = false;
                    carController.GetComponent<Rigidbody>().isKinematic = false;
                }
            }

            // --- State: Still (lines 188-198) ---
            if (__instance.state == OutriggerManager.State.Still)
            {
                if (rccexRb != null)
                {
                    rccexRb.constraints = RigidbodyConstraints.None;
                    rccexRb.angularDrag = 0f;
                    rccexRb.drag = 0f;
                }

                if (__instance.physicsLockCOntroller != null)
                {
                    __instance.physicsLockCOntroller.enabled = true;
                }
                __instance.rccex.outriggersgoingdown = false;
            }

            // --- Input handling (lines 199-226) ---
            if (__instance.input && Input.GetButtonDown("Outrigger Extend"))
            {
                var otm = __instance.GetComponentInParent<OutriggerSystem.OutriggerTotalMovement>();
                if (otm != null)
                {
                    __instance.StopCoroutine(__instance.OutriggerTLUp());
                    __instance.StartCoroutine(__instance.OutriggerTLDown());
                }
                __instance.state = OutriggerManager.State.Extending;
                __instance.audioSource.loop = true;
                __instance.audioSource.Play();
            }
            else if (__instance.input && Input.GetButtonDown("Outrigger Retract"))
            {
                var otm = __instance.GetComponentInParent<OutriggerSystem.OutriggerTotalMovement>();
                if (otm != null)
                {
                    __instance.StopCoroutine(__instance.OutriggerTLDown());
                    __instance.StartCoroutine(__instance.OutriggerTLUp());
                }
                __instance.state = OutriggerManager.State.Contracting;
                __instance.audioSource.loop = true;
                __instance.audioSource.Play();
            }
            else if (Input.GetKeyUp(KeyCode.M) || Input.GetKeyUp(KeyCode.V))
            {
                __instance.state = OutriggerManager.State.Still;
                __instance.audioSource.loop = false;
                __instance.audioSource.Stop();
            }

            // --- Outwards type logic (lines 227-277) ---
            if (__instance.OutriggerType == OutriggerManager.OutriggerTypes.Outwards)
            {
                __instance.BottomPivotPoint.LookAt(__instance.BottomPlate);
                __instance.BottomPivotPoint.Rotate(new Vector3(0f, 0f, 90f));

                if (__instance.state == OutriggerManager.State.Extending)
                {
                    if (carController != null) carController.startcheck = false;

                    if (__instance.MiddleSection.localRotation.eulerAngles.z < __instance.ExtendedRotation.z)
                    {
                        __instance.MiddleSection.Rotate(
                            Vector3.forward * __instance.RotationSpeed * Time.deltaTime, Space.Self);
                    }
                    if (__instance.InnerMiddleSection.localPosition.z < __instance.ExtendedDistance)
                    {
                        __instance.InnerMiddleSection.Translate(
                            Vector3.forward * Time.deltaTime * __instance.MoveSpeed, Space.Self);
                    }
                    if (__instance.InnerMiddleSection.localPosition.z >= __instance.ExtendedDistance
                        && __instance.MiddleSection.localRotation.eulerAngles.z >= __instance.ExtendedRotation.z)
                    {
                        __instance.state = OutriggerManager.State.StillDone;
                        __instance.audioSource.loop = false;
                        __instance.audioSource.Stop();
                        if (__instance.setKinematic != null)
                        {
                            __instance.setKinematic.isKinematic = true;
                            __instance.setKinematic.constraints = RigidbodyConstraints.FreezeAll;
                        }
                    }
                }
                else if (__instance.state == OutriggerManager.State.Contracting)
                {
                    if (carController != null) carController.startcheck = false;

                    if (__instance.MiddleSection.localRotation.eulerAngles.z > __instance.ContractedRotation.z)
                    {
                        __instance.MiddleSection.Rotate(
                            Vector3.back * __instance.RotationSpeed * Time.deltaTime, Space.Self);
                    }
                    if (__instance.InnerMiddleSection.localPosition.z > __instance.ContractedDistance)
                    {
                        __instance.InnerMiddleSection.Translate(
                            Vector3.back * Time.deltaTime * __instance.MoveSpeed * 1.8181818f, Space.Self);
                    }
                    if (__instance.InnerMiddleSection.localPosition.z <= __instance.ContractedDistance
                        && __instance.MiddleSection.localRotation.eulerAngles.z <= __instance.ContractedRotation.z)
                    {
                        __instance.state = OutriggerManager.State.Still;
                        __instance.audioSource.loop = false;
                        __instance.audioSource.Stop();
                        if (__instance.setKinematic != null)
                        {
                            __instance.setKinematic.isKinematic = false;
                        }
                        __instance.setKinematic.constraints = RigidbodyConstraints.None;
                    }
                }
            }

            // --- Verticle type logic (lines 278-319) ---
            if (__instance.OutriggerType != OutriggerManager.OutriggerTypes.Verticle)
            {
                return false; // Skip original
            }

            if (__instance.state == OutriggerManager.State.Contracting)
            {
                if (carController != null) carController.startcheck = false;

                if (__instance.InnerMiddleSection.localPosition.y < __instance.ExtendedDistance)
                {
                    __instance.InnerMiddleSection.Translate(
                        Vector3.up * Time.deltaTime * __instance.MoveSpeed, Space.Self);
                    return false; // Skip original
                }
                __instance.state = OutriggerManager.State.Still;
                __instance.audioSource.loop = false;
                __instance.audioSource.Stop();
                if (__instance.setKinematic != null)
                {
                    __instance.setKinematic.isKinematic = false;
                }
                __instance.setKinematic.constraints = RigidbodyConstraints.None;
            }
            else
            {
                if (__instance.state != OutriggerManager.State.Extending)
                {
                    return false; // Skip original
                }

                if (carController != null) carController.startcheck = false;

                if (__instance.InnerMiddleSection.localPosition.y > __instance.ContractedDistance)
                {
                    __instance.InnerMiddleSection.Translate(
                        Vector3.down * Time.deltaTime * __instance.MoveSpeed, Space.Self);
                    return false; // Skip original
                }
                __instance.state = OutriggerManager.State.StillDone;
                __instance.audioSource.loop = false;
                __instance.audioSource.Stop();
                if (__instance.setKinematic != null)
                {
                    __instance.setKinematic.isKinematic = true;
                    __instance.setKinematic.constraints = RigidbodyConstraints.FreezeAll;
                }
            }

            return false; // Skip original
        }
    }
}
