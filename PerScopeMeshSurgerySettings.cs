using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ScopeHousingMeshSurgery
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
        public bool ReticleOverlayCamera;
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

        private static string FilePath => Path.Combine(ScopeHousingMeshSurgeryPlugin.GetMeshCutCacheDirectory(), "custom_mesh_surgery_settings.json");

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
                ScopeHousingMeshSurgeryPlugin.CustomPlaneOffsetMeters.Value = entry.PlaneOffsetMeters;
                if (!string.IsNullOrWhiteSpace(entry.PlaneNormalAxis))
                    ScopeHousingMeshSurgeryPlugin.CustomPlaneNormalAxis.Value = entry.PlaneNormalAxis;
                ScopeHousingMeshSurgeryPlugin.CustomCutRadius.Value = entry.CutRadius;
                ScopeHousingMeshSurgeryPlugin.CustomShowCutPlane.Value = entry.ShowCutPlane;
                ScopeHousingMeshSurgeryPlugin.CustomShowCutVolume.Value = entry.ShowCutVolume;
                ScopeHousingMeshSurgeryPlugin.CustomCutVolumeOpacity.Value = entry.CutVolumeOpacity;
                if (!string.IsNullOrWhiteSpace(entry.CutMode))
                    ScopeHousingMeshSurgeryPlugin.CustomCutMode.Value = entry.CutMode;
                ScopeHousingMeshSurgeryPlugin.CustomCylinderRadius.Value = entry.CylinderRadius;
                ScopeHousingMeshSurgeryPlugin.CustomMidCylinderRadius.Value = entry.MidCylinderRadius;
                ScopeHousingMeshSurgeryPlugin.CustomMidCylinderPosition.Value = entry.MidCylinderPosition;
                ScopeHousingMeshSurgeryPlugin.CustomFarCylinderRadius.Value = entry.FarCylinderRadius;
                ScopeHousingMeshSurgeryPlugin.CustomPlane1OffsetMeters.Value = entry.Plane1OffsetMeters;
                ScopeHousingMeshSurgeryPlugin.CustomPlane2Position.Value = entry.Plane2Position;
                ScopeHousingMeshSurgeryPlugin.CustomPlane2Radius.Value = entry.Plane2Radius;
                ScopeHousingMeshSurgeryPlugin.CustomPlane3Position.Value = entry.Plane3Position;
                ScopeHousingMeshSurgeryPlugin.CustomPlane3Radius.Value = entry.Plane3Radius;
                ScopeHousingMeshSurgeryPlugin.CustomPlane4Position.Value = entry.Plane4Position;
                ScopeHousingMeshSurgeryPlugin.CustomPlane4Radius.Value = entry.Plane4Radius;
                ScopeHousingMeshSurgeryPlugin.CustomCutStartOffset.Value = entry.CutStartOffset;
                ScopeHousingMeshSurgeryPlugin.CustomCutLength.Value = entry.CutLength;
                ScopeHousingMeshSurgeryPlugin.CustomNearPreserveDepth.Value = entry.NearPreserveDepth;
                ScopeHousingMeshSurgeryPlugin.CustomShowReticle.Value = entry.ShowReticle;
                ScopeHousingMeshSurgeryPlugin.CustomReticleBaseSize.Value = entry.ReticleBaseSize;
                ScopeHousingMeshSurgeryPlugin.CustomReticleOverlayCamera.Value = entry.ReticleOverlayCamera;
                ScopeHousingMeshSurgeryPlugin.CustomRestoreOnUnscope.Value = entry.RestoreOnUnscope;
                ScopeHousingMeshSurgeryPlugin.CustomExpandSearchToWeaponRoot.Value = entry.ExpandSearchToWeaponRoot;
                ScopeHousingMeshSurgeryPlugin.LogInfo($"[CustomMeshSettings] Loaded saved settings for scope '{entry.ScopeKey}' into Custom config entries.");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogWarn($"[CustomMeshSettings] Failed to sync Custom config entries from override: {ex.Message}");
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

            target.PlaneOffsetMeters = ScopeHousingMeshSurgeryPlugin.CustomPlaneOffsetMeters.Value;
            target.PlaneNormalAxis = ScopeHousingMeshSurgeryPlugin.CustomPlaneNormalAxis.Value;
            target.CutRadius = ScopeHousingMeshSurgeryPlugin.CustomCutRadius.Value;
            target.ShowCutPlane = ScopeHousingMeshSurgeryPlugin.CustomShowCutPlane.Value;
            target.ShowCutVolume = ScopeHousingMeshSurgeryPlugin.CustomShowCutVolume.Value;
            target.CutVolumeOpacity = ScopeHousingMeshSurgeryPlugin.CustomCutVolumeOpacity.Value;
            target.CutMode = ScopeHousingMeshSurgeryPlugin.CustomCutMode.Value;
            target.CylinderRadius = ScopeHousingMeshSurgeryPlugin.CustomCylinderRadius.Value;
            target.MidCylinderRadius = ScopeHousingMeshSurgeryPlugin.CustomMidCylinderRadius.Value;
            target.MidCylinderPosition = ScopeHousingMeshSurgeryPlugin.CustomMidCylinderPosition.Value;
            target.FarCylinderRadius = ScopeHousingMeshSurgeryPlugin.CustomFarCylinderRadius.Value;
            target.Plane1OffsetMeters = ScopeHousingMeshSurgeryPlugin.CustomPlane1OffsetMeters.Value;
            target.Plane2Position = ScopeHousingMeshSurgeryPlugin.CustomPlane2Position.Value;
            target.Plane2Radius = ScopeHousingMeshSurgeryPlugin.CustomPlane2Radius.Value;
            target.Plane3Position = ScopeHousingMeshSurgeryPlugin.CustomPlane3Position.Value;
            target.Plane3Radius = ScopeHousingMeshSurgeryPlugin.CustomPlane3Radius.Value;
            target.Plane4Position = ScopeHousingMeshSurgeryPlugin.CustomPlane4Position.Value;
            target.Plane4Radius = ScopeHousingMeshSurgeryPlugin.CustomPlane4Radius.Value;
            target.CutStartOffset = ScopeHousingMeshSurgeryPlugin.CustomCutStartOffset.Value;
            target.CutLength = ScopeHousingMeshSurgeryPlugin.CustomCutLength.Value;
            target.NearPreserveDepth = ScopeHousingMeshSurgeryPlugin.CustomNearPreserveDepth.Value;
            target.ShowReticle = ScopeHousingMeshSurgeryPlugin.CustomShowReticle.Value;
            target.ReticleBaseSize = ScopeHousingMeshSurgeryPlugin.CustomReticleBaseSize.Value;
            target.ReticleOverlayCamera = ScopeHousingMeshSurgeryPlugin.CustomReticleOverlayCamera.Value;
            target.RestoreOnUnscope = ScopeHousingMeshSurgeryPlugin.CustomRestoreOnUnscope.Value;
            target.ExpandSearchToWeaponRoot = ScopeHousingMeshSurgeryPlugin.CustomExpandSearchToWeaponRoot.Value;

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
                ScopeHousingMeshSurgeryPlugin.LogWarn($"[CustomMeshSettings] Failed to load settings json: {ex.Message}");
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
                ScopeHousingMeshSurgeryPlugin.LogError($"[CustomMeshSettings] Failed to save settings json: {ex.Message}");
            }
        }
    }
}
