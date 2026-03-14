using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EmergeNYC.CommunityFixes
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.community.emergenyc.fixes";
        public const string PluginName = "EmergeNYC Community Fixes";
        public const string PluginVersion = "0.2.1";

        internal static new ManualLogSource Logger = null!;
        private Harmony? _harmony;

        private static string? _logPath;
        private static readonly object _logLock = new object();
        private float _nextHeartbeat;

        /// <summary>
        /// Writes directly to a file on disk with immediate flush.
        /// BepInEx logging is completely buffered under Proton and never reaches disk.
        /// Also writes to Unity Debug.Log so it appears in Player.log.
        /// </summary>
        internal static void Log(string message)
        {
            // Always try Unity's logger (goes to Player.log)
            try { Debug.Log($"[CommunityFixes] {message}"); } catch { }

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

            // Set up direct file logging next to the plugin DLL
            try
            {
                string pluginDir = Path.GetDirectoryName(Info.Location) ?? ".";
                _logPath = Path.Combine(pluginDir, "CommunityFixes_diag.log");
                File.WriteAllText(_logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === {PluginName} v{PluginVersion} starting ===\n" +
                    $"[{DateTime.Now:HH:mm:ss.fff}] logPath={_logPath}\n");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to init file logging: {ex.Message}");
            }

            Logger.LogInfo($"=== {PluginName} v{PluginVersion} ===");
            Log($"=== {PluginName} v{PluginVersion} ===");
            Logger.LogInfo("Initializing Harmony patches...");

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
            Logger.LogInfo($"=== Done: {successCount} patch classes applied, {failCount} failed, {methodCount} methods patched ===");
            Log($"=== Done: {successCount} applied, {failCount} failed, {methodCount} methods ===");

            // Log all patched methods for verification
            foreach (var method in _harmony.GetPatchedMethods())
            {
                Log($"  Patched: {method.DeclaringType?.Name}.{method.Name}");
            }
        }

        private void Start()
        {
            Log($"[Plugin.Start] MonoBehaviour Start called, gameObject={gameObject.name} active={gameObject.activeSelf}");
            InvokeRepeating(nameof(Heartbeat), 1f, 10f);
            StartCoroutine(HeartbeatCoroutine());
        }

        private void Heartbeat()
        {
            Log($"[HEARTBEAT-Invoke] alive t={Time.time:F0}");
        }

        private System.Collections.IEnumerator HeartbeatCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(10f);
                Log($"[HEARTBEAT-Coroutine] alive t={Time.time:F0}");
            }
        }

        private void Update()
        {
            if (Time.time > _nextHeartbeat)
            {
                _nextHeartbeat = Time.time + 30f;
                Log($"[HEARTBEAT-Update] alive t={Time.time:F0}");
            }
        }

        private void OnEnable()
        {
            Log($"[Plugin.OnEnable] called");
        }

        private void OnDisable()
        {
            Log($"[Plugin.OnDisable] called");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
