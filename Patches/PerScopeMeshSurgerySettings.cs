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

        internal static float GetPlaneOffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneOffsetMeters : Settings.PlaneOffsetMeters.Value;
        internal static float GetPlane1Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane1Radius : Settings.Plane1Radius.Value;
        internal static float GetPlane1OffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane1OffsetMeters : Settings.Plane1OffsetMeters.Value;
        internal static float GetPlane2Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Position : Settings.Plane2Position.Value;
        internal static float GetPlane2PositionNormalized(float cutLength)
        {
            const float legacyReferenceCutLength = 0.755493f;
            float p2LegacyNormalized = Mathf.Clamp01(GetPlane2Position());
            float anchoredDepth = p2LegacyNormalized * legacyReferenceCutLength;
            return cutLength > 1e-5f ? Mathf.Clamp01(anchoredDepth / cutLength) : 0f;
        }
        internal static float GetPlane2Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Radius : Settings.Plane2Radius.Value;
        internal static float GetPlane3Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Position : Settings.Plane3Position.Value;
        internal static float GetPlane3Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Radius : Settings.Plane3Radius.Value;
        internal static float GetPlane4Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Position : Settings.Plane4Position.Value;
        internal static float GetPlane4Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Radius : Settings.Plane4Radius.Value;
        internal static float GetCutStartOffset() => ActiveScopeOverride != null ? ActiveScopeOverride.CutStartOffset : Settings.CutStartOffset.Value;
        internal static float GetCutLength() => ActiveScopeOverride != null ? ActiveScopeOverride.CutLength : Settings.CutLength.Value;
        internal static float GetNearPreserveDepth() => ActiveScopeOverride != null ? ActiveScopeOverride.NearPreserveDepth : Settings.NearPreserveDepth.Value;
        internal static float GetReticleBaseSize() => ActiveScopeOverride != null ? ActiveScopeOverride.ReticleBaseSize : Settings.ReticleBaseSize.Value;
        internal static bool GetExpandSearchToWeaponRoot() => ActiveScopeOverride != null ? ActiveScopeOverride.ExpandSearchToWeaponRoot : Settings.ExpandSearchToWeaponRoot.Value;


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
                Settings.CustomPlaneOffsetMeters.Value = entry.PlaneOffsetMeters;
                Settings.CustomPlane1Radius.Value = entry.Plane1Radius;
                Settings.CustomPlane1OffsetMeters.Value = entry.Plane1OffsetMeters;
                Settings.CustomPlane2Position.Value = entry.Plane2Position;
                Settings.CustomPlane2Radius.Value = entry.Plane2Radius;
                Settings.CustomPlane3Position.Value = entry.Plane3Position;
                Settings.CustomPlane3Radius.Value = entry.Plane3Radius;
                Settings.CustomPlane4Position.Value = entry.Plane4Position;
                Settings.CustomPlane4Radius.Value = entry.Plane4Radius;
                Settings.CustomCutStartOffset.Value = entry.CutStartOffset;
                Settings.CustomCutLength.Value = entry.CutLength;
                Settings.CustomNearPreserveDepth.Value = entry.NearPreserveDepth;
                Settings.CustomReticleBaseSize.Value = entry.ReticleBaseSize;
                Settings.CustomExpandSearchToWeaponRoot.Value = entry.ExpandSearchToWeaponRoot;
                PiPDisablerPlugin.LogSource.LogInfo($"[CustomMeshSettings] Loaded saved settings for scope '{entry.ScopeKey}' into Custom config entries.");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogSource.LogInfo($"[CustomMeshSettings] Failed to sync Custom config entries from override: {ex.Message}");
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

            target.PlaneOffsetMeters = Settings.CustomPlaneOffsetMeters.Value;
            target.Plane1Radius = Settings.CustomPlane1Radius.Value;
            target.Plane1OffsetMeters = Settings.CustomPlane1OffsetMeters.Value;
            target.Plane2Position = Settings.CustomPlane2Position.Value;
            target.Plane2Radius = Settings.CustomPlane2Radius.Value;
            target.Plane3Position = Settings.CustomPlane3Position.Value;
            target.Plane3Radius = Settings.CustomPlane3Radius.Value;
            target.Plane4Position = Settings.CustomPlane4Position.Value;
            target.Plane4Radius = Settings.CustomPlane4Radius.Value;
            target.CutStartOffset = Settings.CustomCutStartOffset.Value;
            target.CutLength = Settings.CustomCutLength.Value;
            target.NearPreserveDepth = Settings.CustomNearPreserveDepth.Value;
            target.ReticleBaseSize = Settings.CustomReticleBaseSize.Value;
            target.ExpandSearchToWeaponRoot = Settings.CustomExpandSearchToWeaponRoot.Value;
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
                PiPDisablerPlugin.LogSource.LogInfo($"[CustomMeshSettings] Failed to load settings json: {ex.Message}");
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
                PiPDisablerPlugin.LogSource.LogInfo($"[CustomMeshSettings] Failed to save settings json: {ex.Message}");
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
