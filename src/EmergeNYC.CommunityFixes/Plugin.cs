using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using EmergeNYC.ModdingSDK;
using EmergeNYC.CommunityFixes.Patches;

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

            // Init SDK (V12: SDK assembly, CommunityFixes wires bridges)
            EmergeNYCSDK.Init(Log, msg => Logger.LogError(msg));
            WireSDKBridges();

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

        private static void WireSDKBridges()
        {
            // TrafficAPI bridge delegates (V12: CommunityFixes owns the registry impl)
            TrafficAPI.RegisterEmergencyVehicleImpl   = EmergencyVehicleRegistry.Register;
            TrafficAPI.UnregisterEmergencyVehicleImpl = EmergencyVehicleRegistry.Unregister;

            TrafficAPI.GetYieldingCarsImpl = () =>
            {
                var result = new List<TSTrafficAI>();
                if (TSTrafficAI.trafficAIList == null) return result;
                foreach (var car in TSTrafficAI.trafficAIList)
                {
                    if (car != null && YieldStateManager.IsYielding(car.GetInstanceID()))
                        result.Add(car);
                }
                return result;
            };

            TrafficAPI.SetSpawnDensityImpl = density =>
            {
                var spawner = TSTrafficSpawner.mainInstance;
                if (spawner != null)
                    spawner.Amount = Mathf.RoundToInt(spawner.totalAmountOfCars * Mathf.Clamp01(density));
            };

            TrafficAPI.GetSirenActiveVehiclesImpl = () =>
            {
                var result = new List<(Transform, Vector3)>();
                foreach (var kvp in EmergencyVehicleRegistry._activeSirens)
                {
                    var entry = kvp.Value;
                    if (entry.transform == null) continue;
                    if (Time.time - entry.lastUpdated > 2f) continue;
                    var siren = entry.transform.GetComponent<FFD_SirenControl>();
                    if (siren != null && siren.SirenState_Current != FFD_SirenControl.SirenState.Off)
                        result.Add((entry.transform, entry.Velocity));
                }
                return result;
            };

            // CharacterAPI bridge delegates
            CharacterAPI.SpawnAIImpl = (firehouse, engine, role, pos, rot) =>
            {
                if (CharacterManager.instance == null) return null;
                CharacterManager.instance.SpawnAI(firehouse, engine, role, pos, rot);
                return null; // SpawnAI is void; AIManager fires via OnEnable bridge
            };

            CharacterAPI.GetAllCharactersImpl = () =>
                (IReadOnlyList<AIManager>)UnityEngine.Object.FindObjectsOfType<AIManager>();

            // VehicleAPI bridge delegates
            VehicleAPI.GetAllSirenVehiclesImpl = () =>
                (IReadOnlyList<FFD_SirenControl>)UnityEngine.Object.FindObjectsOfType<FFD_SirenControl>();

            // DispatchAPI bridge delegates
            DispatchAPI.ForceEmergencyImpl = type =>
            {
                var em = EmergencyManagerV2.instance;
                if (em?.Procedural == null) return;
                var pes = em.Procedural;
                switch (type)
                {
                    case "EMSCall":       pes.SpawnEMSCall();    break;
                    case "GarageFire":    pes.SpawnGarageFire(); break;
                    case "ApartmentFire": pes.ApartmentFire();   break;
                    case "CarFire":       pes.SpawnCarFire();    break;
                    default: Plugin.Log($"[SDK] Unknown emergency type: {type}"); break;
                }
            };

            DispatchAPI.GetNearestEngineImpl = pos =>
            {
                var pes = EmergencyManagerV2.instance?.Procedural;
                if (pes?.Engines == null) return null;
                Transform best = null!;
                float bestSq = float.MaxValue;
                foreach (var e in pes.Engines)
                {
                    if (e == null) continue;
                    float sq = (e.position - pos).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; best = e; }
                }
                return best;
            };

            DispatchAPI.GetNearestTruckImpl = pos =>
            {
                var pes = EmergencyManagerV2.instance?.Procedural;
                if (pes?.Ladders == null) return null;
                Transform best = null!;
                float bestSq = float.MaxValue;
                foreach (var e in pes.Ladders)
                {
                    if (e == null) continue;
                    float sq = (e.position - pos).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; best = e; }
                }
                return best;
            };

            Log("[SDK] Bridge delegates wired");
        }
    }
}
