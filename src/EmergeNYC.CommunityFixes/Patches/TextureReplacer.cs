using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmergeNYC.CommunityFixes.Patches
{
    // Intercept Resources.Load so resources.assets textures are replaced at load time
    [HarmonyPatch(typeof(Resources), nameof(Resources.Load), new[] { typeof(string), typeof(Type) })]
    public static class ResourcesLoadPatch
    {
        public static void Postfix(ref UnityEngine.Object __result)
        {
            if (__result is Texture2D tex && TextureReplacer.TryGetReplacement(tex.name, out var replacement))
            {
                Plugin.Log($"[TextureReplacer] Resources.Load intercepted: {tex.name}");
                __result = replacement;
            }
        }
    }

    public static class TextureReplacer
    {
        static readonly Dictionary<string, Texture2D> _replacements =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public static int Count => _replacements.Count;

        public static bool TryGetReplacement(string name, out Texture2D replacement)
            => _replacements.TryGetValue(name, out replacement);

        public static void Init(string pluginDir, MonoBehaviour host)
        {
            string folder = Path.Combine(pluginDir, "Textures");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Plugin.Log($"[TextureReplacer] Created Textures folder: {folder}");
                return;
            }

            int loaded = 0;
            foreach (string file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;

                string texName = Path.GetFileNameWithoutExtension(file);
                try
                {
                    byte[] data = File.ReadAllBytes(file);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (ImageConversion.LoadImage(tex, data, false))
                    {
                        tex.name = texName;
                        _replacements[texName] = tex;
                        loaded++;
                        Plugin.Log($"[TextureReplacer] Loaded: {texName} ({tex.width}x{tex.height})");
                    }
                    else
                    {
                        Plugin.Log($"[TextureReplacer] Failed to decode: {file}");
                        UnityEngine.Object.Destroy(tex);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log($"[TextureReplacer] Error loading {file}: {ex.Message}");
                }
            }

            Plugin.Log($"[TextureReplacer] {loaded} replacements loaded from {folder}");
            if (loaded == 0) return;

            SceneManager.sceneLoaded += (scene, _) =>
            {
                Plugin.Log($"[TextureReplacer] Scene loaded: {scene.name}, running ApplyAll");
                ApplyAll();
            };

            // Periodic rescan for Addressables-loaded objects arriving after scene load
            host.StartCoroutine(PeriodicRescan());
        }

        static IEnumerator PeriodicRescan()
        {
            // Initial delay — let scene finish loading
            yield return new WaitForSeconds(3f);
            ApplyAll();

            while (true)
            {
                yield return new WaitForSeconds(10f);
                if (_replacements.Count > 0)
                    ApplyAll();
            }
        }

        public static int ApplyAll()
        {
            if (_replacements.Count == 0) return 0;

            // FindObjectsOfTypeAll finds ALL loaded materials including inactive/pooled objects
            int total = 0;
            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
                total += ApplyToMaterial(mat, mat.name);

            if (total > 0)
                Plugin.Log($"[TextureReplacer] Applied {total} texture replacements across all materials");

            return total;
        }

        public static int ApplyToRenderer(Renderer r)
        {
            if (r == null || _replacements.Count == 0) return 0;
            int count = 0;
            foreach (var mat in r.sharedMaterials)
            {
                if (mat != null)
                    count += ApplyToMaterial(mat, r.gameObject.name);
            }
            return count;
        }

        static int ApplyToMaterial(Material mat, string contextName)
        {
            if (mat == null) return 0;
            int count = 0;
            try
            {
                foreach (string prop in mat.GetTexturePropertyNames())
                {
                    Texture tex = mat.GetTexture(prop);
                    if (tex == null) continue;
                    if (!_replacements.TryGetValue(tex.name, out var replacement)) continue;
                    if (tex.GetInstanceID() == replacement.GetInstanceID()) continue;

                    mat.SetTexture(prop, replacement);
                    count++;
                    Plugin.Log($"[TextureReplacer] {contextName} / {mat.name} / {prop}: {tex.name}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log($"[TextureReplacer] Error on {contextName}: {ex.Message}");
            }
            return count;
        }
    }
}
