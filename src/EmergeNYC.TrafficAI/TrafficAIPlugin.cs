using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.TrafficAI
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.community.emergenyc.fixes", BepInDependency.DependencyFlags.SoftDependency)]
    public class TrafficAIPlugin : BaseUnityPlugin
    {
        public const string PluginGUID    = "com.community.emergenyc.trafficai";
        public const string PluginName    = "EmergeNYC TrafficAI";
        public const string PluginVersion = "0.1.0";

        internal static new ManualLogSource Logger = null!;
        private Harmony? _harmony;

        private static string? _logPath;
        private static readonly object _logLock = new object();

        internal static void Log(string message)
        {
            try { Debug.Log($"[TrafficAI] {message}"); } catch { }
            if (_logPath == null) return;
            try
            {
                lock (_logLock)
                    File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        private void Awake()
        {
            Logger = base.Logger;

            try
            {
                string dir = Path.GetDirectoryName(Info.Location) ?? ".";
                _logPath = Path.Combine(dir, "TrafficAI_diag.log");
                File.WriteAllText(_logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === {PluginName} v{PluginVersion} starting ===\n");
            }
            catch (Exception ex) { Logger.LogError($"Log init failed: {ex.Message}"); }

            Logger.LogInfo($"=== {PluginName} v{PluginVersion} ===");
            Log($"=== {PluginName} v{PluginVersion} ===");

            _harmony = new Harmony(PluginGUID);

            int ok = 0, fail = 0;
            foreach (var type in typeof(TrafficAIPlugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    ok++;
                    Logger.LogInfo($"  [OK] {type.Name}");
                }
                catch (Exception ex)
                {
                    fail++;
                    Logger.LogError($"  [FAIL] {type.Name}: {ex.Message}");
                    Log($"[FAIL] {type.Name}: {ex.Message}");
                }
            }

            Log($"=== Done: {ok} patches ok, {fail} failed, {_harmony.GetPatchedMethods().Count()} methods ===");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
