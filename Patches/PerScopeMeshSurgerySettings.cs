using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PiPDisabler
{
    [Serializable]
    internal sealed class ScopeMeshSurgerySettingsEntry
    {
        public string ScopeKey;
        public float PlaneOffsetMeters;
        public float CutRadius;
        public float Plane1Radius;
        public float Plane1OffsetMeters;
        public float Plane2Position;
        public float Plane2Radius;
        public float Plane3Position;
        public float Plane3Radius;
        public float Plane4Position;
        public float Plane4Radius;
        public float CutStartOffset;
        public float CutLength;
        public float NearPreserveDepth;
        public float ReticleBaseSize;
        public bool ExpandSearchToWeaponRoot;
    }

    [Serializable]
    internal sealed class ScopeMeshSurgerySettingsFile
    {
        public List<ScopeMeshSurgerySettingsEntry> Entries = new List<ScopeMeshSurgerySettingsEntry>();
    }

    internal static class PerScopeMeshSurgerySettings
    {
        private static ScopeMeshSurgerySettingsFile _file = new ScopeMeshSurgerySettingsFile();
        private static bool _loaded;
        private static string _activeScopeKey;

        private static string FilePath => Path.Combine(GetPluginRootDirectory(), "custom_mesh_surgery_settings.json");

        private static ScopeMeshSurgerySettingsEntry ActiveScopeOverride => GetActiveOverride();

        internal static float GetPlaneOffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneOffsetMeters : PiPDisablerPlugin.PlaneOffsetMeters.Value;
        internal static float GetPlane1Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane1Radius : PiPDisablerPlugin.Plane1Radius.Value;
        internal static float GetPlane1OffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane1OffsetMeters : PiPDisablerPlugin.Plane1OffsetMeters.Value;
        internal static float GetPlane2Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Position : PiPDisablerPlugin.Plane2Position.Value;
        internal static float GetPlane2PositionNormalized(float cutLength)
        {
            const float legacyReferenceCutLength = 0.755493f;
            float p2LegacyNormalized = Mathf.Clamp01(GetPlane2Position());
            float anchoredDepth = p2LegacyNormalized * legacyReferenceCutLength;
            return cutLength > 1e-5f ? Mathf.Clamp01(anchoredDepth / cutLength) : 0f;
        }
        internal static float GetPlane2Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Radius : PiPDisablerPlugin.Plane2Radius.Value;
        internal static float GetPlane3Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Position : PiPDisablerPlugin.Plane3Position.Value;
        internal static float GetPlane3Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Radius : PiPDisablerPlugin.Plane3Radius.Value;
        internal static float GetPlane4Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Position : PiPDisablerPlugin.Plane4Position.Value;
        internal static float GetPlane4Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Radius : PiPDisablerPlugin.Plane4Radius.Value;
        internal static float GetCutStartOffset() => ActiveScopeOverride != null ? ActiveScopeOverride.CutStartOffset : PiPDisablerPlugin.CutStartOffset.Value;
        internal static float GetCutLength() => ActiveScopeOverride != null ? ActiveScopeOverride.CutLength : PiPDisablerPlugin.CutLength.Value;
        internal static float GetNearPreserveDepth() => ActiveScopeOverride != null ? ActiveScopeOverride.NearPreserveDepth : PiPDisablerPlugin.NearPreserveDepth.Value;
        internal static float GetReticleBaseSize() => ActiveScopeOverride != null ? ActiveScopeOverride.ReticleBaseSize : PiPDisablerPlugin.ReticleBaseSize.Value;
        internal static bool GetExpandSearchToWeaponRoot() => ActiveScopeOverride != null ? ActiveScopeOverride.ExpandSearchToWeaponRoot : PiPDisablerPlugin.ExpandSearchToWeaponRoot.Value;


        internal static void SetActiveScope(string scopeKey)
        {
            _activeScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? null : scopeKey.Trim();
            SyncCustomConfigFromOverride();
        }

        /// <summary>
        /// Populates the Custom* BepInEx config entries from the active scope's saved JSON values.
        /// This ensures the config manager shows the actual per-scope settings so the user can
        /// see and adjust them without having to remember/re-enter every value manually.
        /// </summary>
        private static void SyncCustomConfigFromOverride()
        {
            var entry = GetActiveOverride();
            if (entry == null) return;

            try
            {
                PiPDisablerPlugin.CustomPlaneOffsetMeters.Value = entry.PlaneOffsetMeters;
                PiPDisablerPlugin.CustomCutRadius.Value = entry.CutRadius;
                PiPDisablerPlugin.CustomPlane1Radius.Value = entry.Plane1Radius;
                PiPDisablerPlugin.CustomPlane1OffsetMeters.Value = entry.Plane1OffsetMeters;
                PiPDisablerPlugin.CustomPlane2Position.Value = entry.Plane2Position;
                PiPDisablerPlugin.CustomPlane2Radius.Value = entry.Plane2Radius;
                PiPDisablerPlugin.CustomPlane3Position.Value = entry.Plane3Position;
                PiPDisablerPlugin.CustomPlane3Radius.Value = entry.Plane3Radius;
                PiPDisablerPlugin.CustomPlane4Position.Value = entry.Plane4Position;
                PiPDisablerPlugin.CustomPlane4Radius.Value = entry.Plane4Radius;
                PiPDisablerPlugin.CustomCutStartOffset.Value = entry.CutStartOffset;
                PiPDisablerPlugin.CustomCutLength.Value = entry.CutLength;
                PiPDisablerPlugin.CustomNearPreserveDepth.Value = entry.NearPreserveDepth;
                PiPDisablerPlugin.CustomReticleBaseSize.Value = entry.ReticleBaseSize;
                PiPDisablerPlugin.CustomExpandSearchToWeaponRoot.Value = entry.ExpandSearchToWeaponRoot;
                PiPDisablerPlugin.LogInfo($"[CustomMeshSettings] Loaded saved settings for scope '{entry.ScopeKey}' into Custom config entries.");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogWarn($"[CustomMeshSettings] Failed to sync Custom config entries from override: {ex.Message}");
            }
        }

        internal static void ClearActiveScope()
        {
            _activeScopeKey = null;
        }

        internal static ScopeMeshSurgerySettingsEntry GetActiveOverride()
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(_activeScopeKey))
                return null;

            for (int i = 0; i < _file.Entries.Count; i++)
            {
                var entry = _file.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ScopeKey))
                    continue;
                if (string.Equals(entry.ScopeKey, _activeScopeKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        internal static bool SaveCustomSettingsForScope(string scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey))
                return false;

            EnsureLoaded();

            ScopeMeshSurgerySettingsEntry target = null;
            for (int i = 0; i < _file.Entries.Count; i++)
            {
                var candidate = _file.Entries[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.ScopeKey))
                    continue;
                if (string.Equals(candidate.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase))
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                target = new ScopeMeshSurgerySettingsEntry { ScopeKey = scopeKey };
                _file.Entries.Add(target);
            }

            target.PlaneOffsetMeters = PiPDisablerPlugin.CustomPlaneOffsetMeters.Value;
            target.CutRadius = PiPDisablerPlugin.CustomCutRadius.Value;
            target.Plane1Radius = PiPDisablerPlugin.CustomPlane1Radius.Value;
            target.Plane1OffsetMeters = PiPDisablerPlugin.CustomPlane1OffsetMeters.Value;
            target.Plane2Position = PiPDisablerPlugin.CustomPlane2Position.Value;
            target.Plane2Radius = PiPDisablerPlugin.CustomPlane2Radius.Value;
            target.Plane3Position = PiPDisablerPlugin.CustomPlane3Position.Value;
            target.Plane3Radius = PiPDisablerPlugin.CustomPlane3Radius.Value;
            target.Plane4Position = PiPDisablerPlugin.CustomPlane4Position.Value;
            target.Plane4Radius = PiPDisablerPlugin.CustomPlane4Radius.Value;
            target.CutStartOffset = PiPDisablerPlugin.CustomCutStartOffset.Value;
            target.CutLength = PiPDisablerPlugin.CustomCutLength.Value;
            target.NearPreserveDepth = PiPDisablerPlugin.CustomNearPreserveDepth.Value;
            target.ReticleBaseSize = PiPDisablerPlugin.CustomReticleBaseSize.Value;
            target.ExpandSearchToWeaponRoot = PiPDisablerPlugin.CustomExpandSearchToWeaponRoot.Value;

            WriteToDisk();
            return true;
        }


        internal static bool DeleteCustomSettingsForScope(string scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey))
                return false;

            EnsureLoaded();

            for (int i = _file.Entries.Count - 1; i >= 0; i--)
            {
                var candidate = _file.Entries[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.ScopeKey))
                    continue;

                if (!string.Equals(candidate.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                _file.Entries.RemoveAt(i);
                WriteToDisk();
                return true;
            }

            return false;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            try
            {
                if (!File.Exists(FilePath))
                    return;

                string json = File.ReadAllText(FilePath);
                var parsed = JsonConvert.DeserializeObject<ScopeMeshSurgerySettingsFile>(json);
                if (parsed != null && parsed.Entries != null)
                    _file = parsed;
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogWarn($"[CustomMeshSettings] Failed to load settings json: {ex.Message}");
                _file = new ScopeMeshSurgerySettingsFile();
            }
        }

        private static void WriteToDisk()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_file, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogError($"[CustomMeshSettings] Failed to save settings json: {ex.Message}");
            }
        }
        private static string GetPluginRootDirectory()
        {
            string pluginDir = null;
            pluginDir = Path.GetDirectoryName(typeof(PerScopeMeshSurgerySettings).Assembly.Location);
            return pluginDir;
        }
    }

}
