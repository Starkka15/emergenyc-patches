using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for ControlFire:
    /// 1. CRITICAL: Removing items from List while iterating forward by index in Updates().
    ///    When an item is removed at index i, the next item shifts to index i, but i++
    ///    means that item is skipped. Over time this causes elements to be missed.
    /// 2. spawnsRenderers and spawnsAudio lists can get out of sync.
    ///    spawnsAudio.Remove(spawnsAudio[i]) can throw IndexOutOfRangeException if audio
    ///    list is shorter than renderers list.
    /// 3. Static lists (spawnsRenderers, spawnsAudio) never cleared between scenes,
    ///    accumulating references to destroyed objects across game sessions.
    /// 4. Updates called every 0.02s via InvokeRepeating but uses Time.deltaTime
    ///    (which is frame deltaTime, not invoke interval) - charring speed varies with framerate.
    /// </summary>
    [HarmonyPatch(typeof(ControlFire))]
    public static class ControlFirePatches
    {
        // Fix 1, 2, 4: Safe reverse iteration and proper list sync
        [HarmonyPatch("Updates")]
        [HarmonyPrefix]
        public static bool Updates_FixListIteration(ControlFire __instance)
        {
            if (!Singleton<PUNGlobals>.Instance.charring)
                return false;

            var cameraXZ = Traverse.Create(__instance).Field("cameraXZ").GetValue<Transform>();
            if (cameraXZ == null) return false;

            Shader.SetGlobalVector("_CameraXZ", cameraXZ.position);

            int burningDelay = __instance.burningDelay;
            int coalDelay = __instance.coalDelay;

            // Fix 4: Use fixed time step (0.02s) instead of Time.deltaTime
            float dt = 0.02f;

            // Fix 1: Iterate backwards so removals don't skip elements
            for (int i = ControlFire.spawnsRenderers.Count - 1; i >= 0; i--)
            {
                var renderer = ControlFire.spawnsRenderers[i];

                // Clean up null/destroyed references
                if (renderer == null)
                {
                    ControlFire.spawnsRenderers.RemoveAt(i);
                    if (i < ControlFire.spawnsAudio.Count)
                        ControlFire.spawnsAudio.RemoveAt(i);
                    continue;
                }

                var burnTarget = renderer.GetComponent<BurnTarget>();
                if (burnTarget == null || burnTarget.extinguishSphere)
                    continue;

                var mat = renderer.material;
                float redPower = mat.GetFloat("_RedPower");
                float greenPower = mat.GetFloat("_GreenPower");

                if (redPower < 1f)
                {
                    mat.SetFloat("_RedPower", redPower + dt / burningDelay);
                }

                if (greenPower < 1f && redPower >= 1f)
                {
                    mat.SetFloat("_GreenPower", greenPower + dt / coalDelay);
                }

                if (greenPower >= 1f)
                {
                    Object.Destroy(renderer.GetComponent<Rigidbody>());
                    Object.Destroy(renderer.GetComponent<AudioSource>());
                    Object.Destroy(burnTarget);

                    ControlFire.spawnsRenderers.RemoveAt(i);
                    // Fix 2: Bounds-check audio list before removal
                    if (i < ControlFire.spawnsAudio.Count)
                        ControlFire.spawnsAudio.RemoveAt(i);
                }
            }

            return false; // Skip original
        }
    }
}
