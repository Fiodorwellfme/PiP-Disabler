using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace PiPDisabler
{
    [Serializable]
    internal sealed class ScopeMeshSurgerySettingsEntry
    {
        public string ScopeKey;
        public float PlaneOffsetMeters;
        public string PlaneNormalAxis;
        public float CutRadius;
        public bool ShowCutPlane;
        public bool ShowCutVolume;
        public float CutVolumeOpacity;
        public string CutMode;
        public float CylinderRadius;
        public float MidCylinderRadius;
        public float MidCylinderPosition;
        public float FarCylinderRadius;
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
        public bool ShowReticle;
        public float ReticleBaseSize;
        public bool RestoreOnUnscope;
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

        private static string FilePath => Path.Combine(PiPDisablerPlugin.GetPluginRootDirectory(), "custom_mesh_surgery_settings.json");

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
                ModSettings.CustomPlaneOffsetMeters.Value = entry.PlaneOffsetMeters;
                if (!string.IsNullOrWhiteSpace(entry.PlaneNormalAxis))
                    ModSettings.CustomPlaneNormalAxis.Value = entry.PlaneNormalAxis;
                ModSettings.CustomCutRadius.Value = entry.CutRadius;
                ModSettings.CustomShowCutPlane.Value = entry.ShowCutPlane;
                ModSettings.CustomShowCutVolume.Value = entry.ShowCutVolume;
                ModSettings.CustomCutVolumeOpacity.Value = entry.CutVolumeOpacity;
                if (!string.IsNullOrWhiteSpace(entry.CutMode))
                    ModSettings.CustomCutMode.Value = entry.CutMode;
                ModSettings.CustomCylinderRadius.Value = entry.CylinderRadius;
                ModSettings.CustomMidCylinderRadius.Value = entry.MidCylinderRadius;
                ModSettings.CustomMidCylinderPosition.Value = entry.MidCylinderPosition;
                ModSettings.CustomFarCylinderRadius.Value = entry.FarCylinderRadius;
                ModSettings.CustomPlane1OffsetMeters.Value = entry.Plane1OffsetMeters;
                ModSettings.CustomPlane2Position.Value = entry.Plane2Position;
                ModSettings.CustomPlane2Radius.Value = entry.Plane2Radius;
                ModSettings.CustomPlane3Position.Value = entry.Plane3Position;
                ModSettings.CustomPlane3Radius.Value = entry.Plane3Radius;
                ModSettings.CustomPlane4Position.Value = entry.Plane4Position;
                ModSettings.CustomPlane4Radius.Value = entry.Plane4Radius;
                ModSettings.CustomCutStartOffset.Value = entry.CutStartOffset;
                ModSettings.CustomCutLength.Value = entry.CutLength;
                ModSettings.CustomNearPreserveDepth.Value = entry.NearPreserveDepth;
                ModSettings.CustomShowReticle.Value = entry.ShowReticle;
                ModSettings.CustomReticleBaseSize.Value = entry.ReticleBaseSize;
                ModSettings.CustomRestoreOnUnscope.Value = entry.RestoreOnUnscope;
                ModSettings.CustomExpandSearchToWeaponRoot.Value = entry.ExpandSearchToWeaponRoot;
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

            target.PlaneOffsetMeters = ModSettings.CustomPlaneOffsetMeters.Value;
            target.PlaneNormalAxis = ModSettings.CustomPlaneNormalAxis.Value;
            target.CutRadius = ModSettings.CustomCutRadius.Value;
            target.ShowCutPlane = ModSettings.CustomShowCutPlane.Value;
            target.ShowCutVolume = ModSettings.CustomShowCutVolume.Value;
            target.CutVolumeOpacity = ModSettings.CustomCutVolumeOpacity.Value;
            target.CutMode = ModSettings.CustomCutMode.Value;
            target.CylinderRadius = ModSettings.CustomCylinderRadius.Value;
            target.MidCylinderRadius = ModSettings.CustomMidCylinderRadius.Value;
            target.MidCylinderPosition = ModSettings.CustomMidCylinderPosition.Value;
            target.FarCylinderRadius = ModSettings.CustomFarCylinderRadius.Value;
            target.Plane1OffsetMeters = ModSettings.CustomPlane1OffsetMeters.Value;
            target.Plane2Position = ModSettings.CustomPlane2Position.Value;
            target.Plane2Radius = ModSettings.CustomPlane2Radius.Value;
            target.Plane3Position = ModSettings.CustomPlane3Position.Value;
            target.Plane3Radius = ModSettings.CustomPlane3Radius.Value;
            target.Plane4Position = ModSettings.CustomPlane4Position.Value;
            target.Plane4Radius = ModSettings.CustomPlane4Radius.Value;
            target.CutStartOffset = ModSettings.CustomCutStartOffset.Value;
            target.CutLength = ModSettings.CustomCutLength.Value;
            target.NearPreserveDepth = ModSettings.CustomNearPreserveDepth.Value;
            target.ShowReticle = ModSettings.CustomShowReticle.Value;
            target.ReticleBaseSize = ModSettings.CustomReticleBaseSize.Value;
            target.RestoreOnUnscope = ModSettings.CustomRestoreOnUnscope.Value;
            target.ExpandSearchToWeaponRoot = ModSettings.CustomExpandSearchToWeaponRoot.Value;

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
    }
}
