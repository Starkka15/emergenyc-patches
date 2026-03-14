using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for MountPoint:
    /// 1. canBeMounted setter (lines 139-158): empty catch block swallows all exceptions,
    ///    hiding hose connection failures and making them impossible to debug.
    ///    Fix: replace the setter with one that logs the exception instead of swallowing it.
    /// 2. OnDestroy (line 374-377): calls mountPoints.Remove(this) without null-checking
    ///    the static mountPoints list. If OnDestroy runs before any MountPoint.Awake
    ///    (e.g. during early scene cleanup), mountPoints is null and this throws NRE.
    ///    Fix: null-check mountPoints before removing.
    /// </summary>
    public static class MountPointPatches
    {
        // Fix 1: canBeMounted setter - log exceptions instead of swallowing them
        [HarmonyPatch(typeof(MountPoint), "canBeMounted", MethodType.Setter)]
        public static class CanBeMountedSetterPatch
        {
            private static bool _loggedOnce;

            public static bool Prefix(MountPoint __instance, bool value)
            {
                try
                {
                    if (!_loggedOnce)
                    {
                        _loggedOnce = true;
                        Plugin.Log(
                            $"[MountPoint] canBeMounted setter: value={value} name={__instance.name}");
                    }

                    __instance.p_canBeMounted = value;

                    // Fire the OnMountPointUpdate event via Traverse since it's a private event backing field
                    var updateDelegate = Traverse.Create(__instance).Field("OnMountPointUpdate")
                        .GetValue<MountPoint.MountPointStatusUpdate>();
                    if (updateDelegate != null)
                    {
                        updateDelegate(value, __instance);
                    }

                    if (value)
                    {
                        __instance.cacheLinkedHose = __instance.linkedHose;
                        if (__instance.trackNozzle && __instance.linkedHose != null
                            && __instance.nozzle != null && __instance.nozzle.gameObject.activeSelf)
                        {
                            __instance.DropNozzle();
                        }
                        __instance.linkedHose = null;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CommunityFixes] MountPoint.canBeMounted setter threw on '{__instance.name}': {ex}");
                }

                return false; // Skip original
            }
        }

        // Fix 2: OnDestroy - null-check mountPoints before removing
        [HarmonyPatch(typeof(MountPoint), "OnDestroy")]
        public static class OnDestroyPatch
        {
            public static bool Prefix(MountPoint __instance)
            {
                if (MountPoint.mountPoints != null)
                {
                    MountPoint.mountPoints.Remove(__instance);
                }

                return false; // Skip original
            }
        }
    }
}
