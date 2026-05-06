using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmergeNYC.ModdingSDK
{
    /// <summary>
    /// Character and AI lifecycle hooks.
    /// Bridge delegates wired by CommunityFixes at init.
    /// </summary>
    public static class CharacterAPI
    {
        // ── Events ──────────────────────────────────────────────────────────
        public static event Action<AIManager>? OnCharacterSpawned;
        public static event Action<AIManager>? OnAITakeControl;
        public static event Action<AIManager>? OnPlayerTakeControl;

        // ── Bridge delegates ─────────────────────────────────────────────────
        public static Func<string, string, string, Vector3, Quaternion, AIManager?>? SpawnAIImpl;
        public static Func<IReadOnlyList<AIManager>>? GetAllCharactersImpl;

        // ── Public helpers ───────────────────────────────────────────────────

        /// <summary>Spawn an AI character at the given position.</summary>
        public static AIManager? SpawnAI(string firehouse, string engine, string role,
                                         Vector3 position, Quaternion rotation) =>
            SpawnAIImpl?.Invoke(firehouse, engine, role, position, rotation);

        /// <summary>Returns all active AIManager instances in the scene.</summary>
        public static IReadOnlyList<AIManager> GetAllCharacters() =>
            GetAllCharactersImpl?.Invoke() ?? Array.Empty<AIManager>();

        // ── Internal raise ───────────────────────────────────────────────────
        internal static void RaiseCharacterSpawned(AIManager ai) =>
            EmergeNYCSDK.SafeRaise(OnCharacterSpawned, ai, nameof(OnCharacterSpawned));

        internal static void RaiseAITakeControl(AIManager ai) =>
            EmergeNYCSDK.SafeRaise(OnAITakeControl, ai, nameof(OnAITakeControl));

        internal static void RaisePlayerTakeControl(AIManager ai) =>
            EmergeNYCSDK.SafeRaise(OnPlayerTakeControl, ai, nameof(OnPlayerTakeControl));
    }
}
