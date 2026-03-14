using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes.Patches
{
    /// <summary>
    /// Fixes for SelectionManager:
    /// 1. RemoveGameObjectFromSelection modifies selectedGameObjects list during foreach enumeration.
    ///    Calling selectedGameObjects.Remove() inside a foreach over selectedGameObjects throws
    ///    InvalidOperationException ("Collection was modified; enumeration operation may not execute").
    ///    Fix: Replace with backwards index iteration so removals don't invalidate the loop.
    /// </summary>
    [HarmonyPatch(typeof(SelectionManager))]
    public static class SelectionManagerPatches
    {
        // Fix 1: RemoveGameObjectFromSelection - safe backwards iteration
        [HarmonyPatch(nameof(SelectionManager.RemoveGameObjectFromSelection))]
        [HarmonyPrefix]
        public static bool RemoveGameObjectFromSelection_SafeIteration(SelectionManager __instance, GameObject removedObject)
        {
            Plugin.Log(
                $"[SelectionManager] RemoveGameObjectFromSelection: removing={removedObject?.name ?? "NULL"}" +
                $" selectedCount={__instance.selectedGameObjects?.Count ?? -1}");

            if (__instance.selectedGameObjects.Count <= 0)
                return false;

            for (int i = __instance.selectedGameObjects.Count - 1; i >= 0; i--)
            {
                var selected = __instance.selectedGameObjects[i];
                if (selected.gameObject == removedObject)
                {
                    // detachSelection is private, invoke via Traverse
                    Traverse.Create(__instance).Method("detachSelection", selected).GetValue();
                    __instance.selectedGameObjects.RemoveAt(i);
                    Plugin.Log($"[SelectionManager] Removed at index {i}");
                }
            }

            return false; // Skip original
        }
    }
}
