using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.PoliceEnhancement
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.community.emergenyc.policeenhancement";
        public const string PluginName = "EmergeNYC Police Enhancement";
        public const string PluginVersion = "0.1.0";

        internal static new ManualLogSource Logger = null!;
        private Harmony? _harmony;

        private static string? _logPath;
        private static readonly object _logLock = new object();

        internal static void Log(string message)
        {
            try { Debug.Log($"[PoliceEnhancement] {message}"); } catch { }

            if (_logPath == null) return;
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                lock (_logLock)
                {
                    File.AppendAllText(_logPath, line);
                }
            }
            catch { }
        }

        private void Awake()
        {
            Logger = base.Logger;

            try
            {
                string pluginDir = Path.GetDirectoryName(Info.Location) ?? ".";
                _logPath = Path.Combine(pluginDir, "PoliceEnhancement_diag.log");
                File.WriteAllText(_logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === {PluginName} v{PluginVersion} starting ===\n");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to init file logging: {ex.Message}");
            }

            Logger.LogInfo($"=== {PluginName} v{PluginVersion} ===");
            Log($"=== {PluginName} v{PluginVersion} ===");

            _harmony = new Harmony(PluginGUID);

            int successCount = 0;
            int failCount = 0;

            foreach (var type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                    continue;

                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    successCount++;
                    Logger.LogInfo($"  [OK] {type.Name}");
                }
                catch (Exception ex)
                {
                    failCount++;
                    Logger.LogError($"  [FAIL] {type.Name}: {ex.Message}");
                    Log($"[FAIL] {type.Name}: {ex.Message}");
                }
            }

            int methodCount = _harmony.GetPatchedMethods().Count();
            Logger.LogInfo($"=== Done: {successCount} applied, {failCount} failed, {methodCount} methods ===");
            Log($"=== Done: {successCount} applied, {failCount} failed, {methodCount} methods ===");
        }

        private void Start()
        {
            // Attach runtime components to the plugin GameObject
            gameObject.AddComponent<Components.TrafficStopController>();
            gameObject.AddComponent<Components.PoliceDispatchManager>();
            gameObject.AddComponent<Components.PoliceScenePerimeter>();
            Log("[Plugin.Start] Runtime components attached");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
