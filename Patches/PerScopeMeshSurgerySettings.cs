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
                PiPDisablerPlugin.CustomPlaneOffsetMeters.Value = entry.PlaneOffsetMeters;
                if (!string.IsNullOrWhiteSpace(entry.PlaneNormalAxis))
                    PiPDisablerPlugin.CustomPlaneNormalAxis.Value = entry.PlaneNormalAxis;
                PiPDisablerPlugin.CustomCutRadius.Value = entry.CutRadius;
                PiPDisablerPlugin.CustomShowCutPlane.Value = entry.ShowCutPlane;
                PiPDisablerPlugin.CustomShowCutVolume.Value = entry.ShowCutVolume;
                PiPDisablerPlugin.CustomCutVolumeOpacity.Value = entry.CutVolumeOpacity;
                if (!string.IsNullOrWhiteSpace(entry.CutMode))
                    PiPDisablerPlugin.CustomCutMode.Value = entry.CutMode;
                PiPDisablerPlugin.CustomCylinderRadius.Value = entry.CylinderRadius;
                PiPDisablerPlugin.CustomMidCylinderRadius.Value = entry.MidCylinderRadius;
                PiPDisablerPlugin.CustomMidCylinderPosition.Value = entry.MidCylinderPosition;
                PiPDisablerPlugin.CustomFarCylinderRadius.Value = entry.FarCylinderRadius;
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
                PiPDisablerPlugin.CustomShowReticle.Value = entry.ShowReticle;
                PiPDisablerPlugin.CustomReticleBaseSize.Value = entry.ReticleBaseSize;
                PiPDisablerPlugin.CustomRestoreOnUnscope.Value = entry.RestoreOnUnscope;
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
            target.PlaneNormalAxis = PiPDisablerPlugin.CustomPlaneNormalAxis.Value;
            target.CutRadius = PiPDisablerPlugin.CustomCutRadius.Value;
            target.ShowCutPlane = PiPDisablerPlugin.CustomShowCutPlane.Value;
            target.ShowCutVolume = PiPDisablerPlugin.CustomShowCutVolume.Value;
            target.CutVolumeOpacity = PiPDisablerPlugin.CustomCutVolumeOpacity.Value;
            target.CutMode = PiPDisablerPlugin.CustomCutMode.Value;
            target.CylinderRadius = PiPDisablerPlugin.CustomCylinderRadius.Value;
            target.MidCylinderRadius = PiPDisablerPlugin.CustomMidCylinderRadius.Value;
            target.MidCylinderPosition = PiPDisablerPlugin.CustomMidCylinderPosition.Value;
            target.FarCylinderRadius = PiPDisablerPlugin.CustomFarCylinderRadius.Value;
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
            target.ShowReticle = PiPDisablerPlugin.CustomShowReticle.Value;
            target.ReticleBaseSize = PiPDisablerPlugin.CustomReticleBaseSize.Value;
            target.RestoreOnUnscope = PiPDisablerPlugin.CustomRestoreOnUnscope.Value;
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
    }
}
