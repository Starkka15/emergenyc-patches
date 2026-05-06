using System;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Entry point for the EmergeNYC Modding SDK.
    /// Call EmergeNYCSDK.Init() once from your BepInEx plugin's Awake.
    /// All subsystem APIs are accessible as static classes in this namespace.
    ///
    /// V12: This assembly (EmergeNYC.ModdingSDK.dll) is the only surface external mods reference.
    ///      Internal patch classes in CommunityFixes are not SDK surface.
    /// V13: All events are static Action&lt;T&gt; — null-safe invoked, exceptions caught.
    /// </summary>
    public static class EmergeNYCSDK
    {
        public const string SDKVersion = "1.0.0";

        internal static Action<string>? LogFn;
        internal static Action<string>? LogErrorFn;

        private static bool _initialized;

        /// <summary>
        /// Initialize the SDK. Called by CommunityFixes on startup.
        /// External mods may also call this safely — it is idempotent.
        /// </summary>
        public static void Init(Action<string>? logFn = null, Action<string>? logErrorFn = null)
        {
            if (_initialized) return;
            _initialized = true;

            LogFn      = logFn;
            LogErrorFn = logErrorFn;

            Log($"EmergeNYC Modding SDK v{SDKVersion} initialized");
        }

        internal static void Log(string msg)
        {
            try { LogFn?.Invoke($"[SDK] {msg}"); } catch { }
        }

        internal static void LogError(string msg)
        {
            try
            {
                LogErrorFn?.Invoke($"[SDK][ERROR] {msg}");
                Debug.LogError($"[EmergeNYCSDK] {msg}");
            }
            catch { }
        }

        /// <summary>
        /// Safely invoke an event, catching and logging any handler exceptions.
        /// V13: No event ever throws to the game loop.
        /// </summary>
        internal static void SafeRaise<T>(Action<T>? evt, T arg, string evtName)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action<T>)d)(arg); }
                catch (Exception ex) { LogError($"{evtName} handler threw: {ex}"); }
            }
        }

        internal static void SafeRaise(Action? evt, string evtName)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception ex) { LogError($"{evtName} handler threw: {ex}"); }
            }
        }

        internal static void SafeRaise<T1, T2>(Action<T1, T2>? evt, T1 a1, T2 a2, string evtName)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action<T1, T2>)d)(a1, a2); }
                catch (Exception ex) { LogError($"{evtName} handler threw: {ex}"); }
            }
        }

        internal static void SafeRaise<T1, T2, T3>(Action<T1, T2, T3>? evt, T1 a1, T2 a2, T3 a3, string evtName)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action<T1, T2, T3>)d)(a1, a2, a3); }
                catch (Exception ex) { LogError($"{evtName} handler threw: {ex}"); }
            }
        }
    }
}
