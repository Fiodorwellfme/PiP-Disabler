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
        public float ReticleSizeMultiplier = 1f;
        public float MeshReticleMinScale;
        public float MeshReticleMaxScale;
        public float WeaponScaleMinMagnification;
        public float WeaponScaleMaxMagnification;
        public float WeaponScaleMultiplier = 1f;
        public float VisualRecoilCompensation;
        public float VignetteOpacity;
        public float VignetteRadius;
        public float VignetteSoftness;
        public bool ExpandSearchToWeaponRoot;
        public float ZoomMultiplier = 1f;
        public float CutWidthMultiplier = 1f;
        public bool CutManual;
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
        // Last scope aimed with: F12 edits made after leaving the scope still belong to it.
        private static string _lastScopeKey;
        internal static string LastScopeKey => _lastScopeKey;
        internal static string ActiveScopeKey => _activeScopeKey;
        private static bool _syncingCustomConfig;

        // Bundled defaults ship in the plugin folder and are replaced by every mod update/reinstall.
        private static string FilePath => Path.Combine(GetPluginRootDirectory(), "custom_mesh_surgery_settings.json");
        // The player's own per-scope edits live in BepInEx/config, which updates never touch.
        private static string UserFilePath => Path.Combine(BepInEx.Paths.ConfigPath, "PiP-Disabler.scope-settings.json");
        private static readonly HashSet<string> _userKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
        internal static float GetPlane3Radius() => ScaleOuterRadius(ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Radius : Settings.Plane3Radius.Value);
        internal static float GetPlane4Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Position : Settings.Plane4Position.Value;
        internal static float GetPlane4Radius() => ScaleOuterRadius(ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Radius : Settings.Plane4Radius.Value);
        internal static float GetCutStartOffset() => ActiveScopeOverride != null ? ActiveScopeOverride.CutStartOffset : Settings.CutStartOffset.Value;
        internal static float GetCutLength() => ActiveScopeOverride != null ? ActiveScopeOverride.CutLength : Settings.CutLength.Value;
        internal static float GetNearPreserveDepth() => ActiveScopeOverride != null ? ActiveScopeOverride.NearPreserveDepth : Settings.NearPreserveDepth.Value;
        internal static float GetReticleBaseSize()
        {
            var entry = ActiveScopeOverride;
            if (entry == null)
                return Settings.ReticleBaseSize.Value;

            float multiplier = entry.ReticleSizeMultiplier > 0f ? entry.ReticleSizeMultiplier : 1f;
            return entry.ReticleBaseSize * multiplier;
        }
        internal static float GetMeshReticleMinScale() => ActiveScopeOverride != null ? ActiveScopeOverride.MeshReticleMinScale : Settings.MeshReticleMinScale.Value;
        internal static float GetMeshReticleMaxScale() => ActiveScopeOverride != null ? ActiveScopeOverride.MeshReticleMaxScale : Settings.MeshReticleMaxScale.Value;
        internal static bool TryGetWeaponScale(out float minMagnificationScale, out float maxMagnificationScale)
        {
            minMagnificationScale = 0f;
            maxMagnificationScale = 0f;
            var entry = ActiveScopeOverride;
            if (entry == null || entry.WeaponScaleMinMagnification <= 0f || entry.WeaponScaleMaxMagnification <= 0f)
                return false;

            float multiplier = entry.WeaponScaleMultiplier > 0f ? entry.WeaponScaleMultiplier : 1f;
            minMagnificationScale = entry.WeaponScaleMinMagnification * multiplier;
            maxMagnificationScale = entry.WeaponScaleMaxMagnification * multiplier;
            return true;
        }
        internal static float GetVignetteOpacity() => GetPositiveOrDefault(GetLiveVignetteValue(Settings.CustomVignetteOpacity.Value, ActiveScopeOverride?.VignetteOpacity ?? 0f), Settings.VignetteOpacity.Value);
        internal static float GetVignetteRadius() => GetPositiveOrDefault(GetLiveVignetteValue(Settings.CustomVignetteRadius.Value, ActiveScopeOverride?.VignetteRadius ?? 0f), Settings.VignetteRadius.Value);
        internal static float GetVignetteSoftness() => GetPositiveOrDefault(GetLiveVignetteValue(Settings.CustomVignetteSoftness.Value, ActiveScopeOverride?.VignetteSoftness ?? 0f), Settings.VignetteSoftness.Value);
        internal static float GetVisualRecoilCompensation()
        {
            var entry = ActiveScopeOverride;
            return entry != null && Mathf.Abs(entry.VisualRecoilCompensation) > 0.0001f
                ? entry.VisualRecoilCompensation
                : Settings.VisualRecoilCompensation.Value;
        }
        // "구멍 넓이" narrows only the outer part of the hole (planes 3/4, in front of the scope where
        // its outside shape gets cut). Plane 2 sits inside the tube; shrinking it (2.5.5) turned the
        // lens into a pinhole and the scope interior went black. Never narrower than plane 2.
        private static float ScaleOuterRadius(float radius)
        {
            float inner = ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Radius : Settings.Plane2Radius.Value;
            return Mathf.Max(radius * GetCutWidthMultiplier(), inner);
        }

        /// <summary>Automatic hole shape unless turned off globally or this scope chose its manual values.</summary>
        internal static bool IsAutoCut()
        {
            if (!Settings.AutoCutShape.Value) return false;
            var entry = ActiveScopeOverride;
            return entry == null || !entry.CutManual;
        }

        internal static float GetCutWidthMultiplierRaw() => GetCutWidthMultiplier();

        /// <summary>"구멍 넓이": scales the hole's front radii (1 = as set).</summary>
        internal static float GetCutWidthMultiplier()
        {
            var entry = ActiveScopeOverride;
            return entry != null && entry.CutWidthMultiplier > 0.01f ? entry.CutWidthMultiplier : 1f;
        }

        /// <summary>Extra zoom for the active scope (1 = the scope's own magnification).</summary>
        internal static float GetZoomMultiplier()
        {
            var entry = ActiveScopeOverride;
            return entry != null && entry.ZoomMultiplier > 0.01f ? entry.ZoomMultiplier : 1f;
        }
        internal static bool GetExpandSearchToWeaponRoot() => ActiveScopeOverride != null ? ActiveScopeOverride.ExpandSearchToWeaponRoot : Settings.ExpandSearchToWeaponRoot.Value;


        internal static void SetActiveScope(string scopeKey)
        {
            _activeScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? null : scopeKey.Trim();
            if (_activeScopeKey != null)
                _lastScopeKey = _activeScopeKey;
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

            try
            {
                _syncingCustomConfig = true;
                bool hasEntry = entry != null;
                if (!hasEntry)
                {
                    // No saved values: show the values actually in use (global defaults) instead of
                    // whatever the previous scope left behind, so saving starts from the real state.
                    entry = new ScopeMeshSurgerySettingsEntry { ScopeKey = _activeScopeKey };
                    CopyGlobalSettingsTo(entry);
                    // Same size as now: once saved, the global multipliers no longer stack on this scope.
                    entry.WeaponScaleMinMagnification = Settings.ManualWeaponScale.Value * Settings.GlobalScopeScalingMultiplier.Value;
                    entry.WeaponScaleMaxMagnification = entry.WeaponScaleMinMagnification;
                    entry.ReticleSizeMultiplier = Settings.GlobalReticleScalingMultiplier.Value;
                    entry.VignetteOpacity = 0f;
                    entry.VignetteRadius = 0f;
                    entry.VignetteSoftness = 0f;
                }

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
                Settings.CustomReticleSizeMultiplier.Value = entry.ReticleSizeMultiplier > 0f ? entry.ReticleSizeMultiplier : 1f;
                Settings.CustomMeshReticleMinScale.Value = entry.MeshReticleMinScale;
                Settings.CustomMeshReticleMaxScale.Value = entry.MeshReticleMaxScale;
                Settings.CustomWeaponScaleMinMagnification.Value = entry.WeaponScaleMinMagnification;
                Settings.CustomWeaponScaleMaxMagnification.Value = entry.WeaponScaleMaxMagnification;
                Settings.CustomWeaponScaleMultiplier.Value = entry.WeaponScaleMultiplier > 0f ? entry.WeaponScaleMultiplier : 1f;
                Settings.CustomVisualRecoilCompensation.Value = entry.VisualRecoilCompensation;
                Settings.CustomVignetteOpacity.Value = entry.VignetteOpacity;
                Settings.CustomVignetteRadius.Value = entry.VignetteRadius;
                Settings.CustomVignetteSoftness.Value = entry.VignetteSoftness;
                Settings.CustomExpandSearchToWeaponRoot.Value = entry.ExpandSearchToWeaponRoot;
                Settings.CustomZoomMultiplier.Value = entry.ZoomMultiplier > 0.01f ? entry.ZoomMultiplier : 1f;
                Settings.CustomCutWidthMultiplier.Value = entry.CutWidthMultiplier > 0.01f ? entry.CutWidthMultiplier : 1f;
                Settings.CustomCutManual.Value = entry.CutManual;
                PiPDisablerPlugin.DebugLogInfo(hasEntry
                    ? $"[CustomMeshSettings] Loaded saved settings for scope '{entry.ScopeKey}' into Custom config entries."
                    : $"[CustomMeshSettings] No saved settings for scope '{entry.ScopeKey}' — Custom config entries show the defaults.");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.DebugLogInfo($"[CustomMeshSettings] Failed to sync Custom config entries from override: {ex.Message}");
            }
            finally
            {
                _syncingCustomConfig = false;
            }
        }

        /// <summary>Sets the active scope key only (no F12 sync, no "last scope"): for background work.</summary>
        internal static void SetActiveScopeQuiet(string scopeKey)
        {
            _activeScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? null : scopeKey.Trim();
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

            CopyCustomTo(target);
            MarkUserEdited(scopeKey);
            WriteToDisk();
            return true;
        }

        private static void CopyCustomTo(ScopeMeshSurgerySettingsEntry target)
        {
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
            target.ReticleSizeMultiplier = Settings.CustomReticleSizeMultiplier.Value;
            target.MeshReticleMinScale = Settings.CustomMeshReticleMinScale.Value;
            target.MeshReticleMaxScale = Settings.CustomMeshReticleMaxScale.Value;
            target.WeaponScaleMinMagnification = Settings.CustomWeaponScaleMinMagnification.Value;
            target.WeaponScaleMaxMagnification = Settings.CustomWeaponScaleMaxMagnification.Value;
            target.WeaponScaleMultiplier = Settings.CustomWeaponScaleMultiplier.Value;
            target.VisualRecoilCompensation = Settings.CustomVisualRecoilCompensation.Value;
            target.VignetteOpacity = Settings.CustomVignetteOpacity.Value;
            target.VignetteRadius = Settings.CustomVignetteRadius.Value;
            target.VignetteSoftness = Settings.CustomVignetteSoftness.Value;
            target.ExpandSearchToWeaponRoot = Settings.CustomExpandSearchToWeaponRoot.Value;
            target.ZoomMultiplier = Settings.CustomZoomMultiplier.Value;
            target.CutWidthMultiplier = Settings.CustomCutWidthMultiplier.Value;
            target.CutManual = Settings.CustomCutManual.Value;
        }

        internal static bool IsSyncing => _syncingCustomConfig;

        private const float WriteDelay = 0.3f;
        private static float _writeDueAt = -1f;

        /// <summary>
        /// A per-scope slider moved while aiming: put the values into the active scope's entry
        /// right away (the getters read it every frame) and save to disk shortly after.
        /// </summary>
        internal static bool ApplyLiveEditFromCustom()
        {
            if (_syncingCustomConfig) return false;
            string key = !string.IsNullOrWhiteSpace(_activeScopeKey) ? _activeScopeKey : _lastScopeKey;
            if (string.IsNullOrWhiteSpace(key)) return false;

            EnsureLoaded();
            CopyCustomTo(GetOrCreateEntry(key));
            MarkUserEdited(key);
            _writeKey = key;
            _writeDueAt = Time.realtimeSinceStartup + WriteDelay;
            SettingsApplyGate.NoteChange();
            return true;
        }

        internal static void TickPendingWrite()
        {
            if (_writeDueAt < 0f || Time.realtimeSinceStartup < _writeDueAt) return;
            if (!SettingsApplyGate.CanApply) return; // write once when the F12 window closes
            FlushPendingWrite();
        }

        /// <summary>Writes a pending auto-save now (also called on game exit).</summary>
        internal static void FlushPendingWrite()
        {
            if (_writeDueAt < 0f) return;
            _writeDueAt = -1f;
            WriteToDisk();
            PiPDisablerPlugin.LogSource.LogInfo($"[CustomMeshSettings] Auto-saved per-scope settings ('{_writeKey ?? "?"}').");
        }

        private static string _writeKey;

        /// <summary>The global multipliers only apply to scopes without their own saved settings.</summary>
        internal static float GetGlobalReticleMultiplier()
            => ActiveScopeOverride != null ? 1f : Settings.GlobalReticleScalingMultiplier.Value;

        internal static void SaveActiveScopeVisualSettings()
        {
            if (_syncingCustomConfig || string.IsNullOrWhiteSpace(_activeScopeKey))
                return;

            EnsureLoaded();

            var target = GetOrCreateEntry(_activeScopeKey);
            MarkUserEdited(_activeScopeKey);
            target.VignetteOpacity = Settings.CustomVignetteOpacity.Value;
            target.VignetteRadius = Settings.CustomVignetteRadius.Value;
            target.VignetteSoftness = Settings.CustomVignetteSoftness.Value;
            WriteToDisk();
        }


        /// <summary>Removes the player's own settings for the scope (its bundled default, if any, applies again).</summary>
        internal static bool DeleteCustomSettingsForScope(string scopeKey)
        {
            if (string.IsNullOrWhiteSpace(scopeKey))
                return false;

            EnsureLoaded();
            if (!_userKeys.Remove(scopeKey))
                return false;

            _file.Entries.RemoveAll(e => e != null && string.Equals(e.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase));
            WriteToDisk();
            _loaded = false; // re-merge so the bundled default for this scope is back
            EnsureLoaded();
            return true;
        }

        private static ScopeMeshSurgerySettingsEntry GetOrCreateEntry(string scopeKey)
        {
            for (int i = 0; i < _file.Entries.Count; i++)
            {
                var candidate = _file.Entries[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.ScopeKey))
                    continue;
                if (string.Equals(candidate.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            var target = new ScopeMeshSurgerySettingsEntry { ScopeKey = scopeKey };
            CopyGlobalSettingsTo(target);
            _file.Entries.Add(target);
            return target;
        }

        private static void CopyGlobalSettingsTo(ScopeMeshSurgerySettingsEntry target)
        {
            target.PlaneOffsetMeters = Settings.PlaneOffsetMeters.Value;
            target.Plane1Radius = Settings.Plane1Radius.Value;
            target.Plane1OffsetMeters = Settings.Plane1OffsetMeters.Value;
            target.Plane2Position = Settings.Plane2Position.Value;
            target.Plane2Radius = Settings.Plane2Radius.Value;
            target.Plane3Position = Settings.Plane3Position.Value;
            target.Plane3Radius = Settings.Plane3Radius.Value;
            target.Plane4Position = Settings.Plane4Position.Value;
            target.Plane4Radius = Settings.Plane4Radius.Value;
            target.CutStartOffset = Settings.CutStartOffset.Value;
            target.CutLength = Settings.CutLength.Value;
            target.NearPreserveDepth = Settings.NearPreserveDepth.Value;
            target.ReticleBaseSize = Settings.ReticleBaseSize.Value;
            target.ReticleSizeMultiplier = 1f;
            target.MeshReticleMinScale = Settings.MeshReticleMinScale.Value;
            target.MeshReticleMaxScale = Settings.MeshReticleMaxScale.Value;
            target.WeaponScaleMinMagnification = 0f;
            target.WeaponScaleMaxMagnification = 0f;
            target.WeaponScaleMultiplier = 1f;
            target.VisualRecoilCompensation = 0f;
            target.VignetteOpacity = Settings.CustomVignetteOpacity.Value;
            target.VignetteRadius = Settings.CustomVignetteRadius.Value;
            target.VignetteSoftness = Settings.CustomVignetteSoftness.Value;
            target.ExpandSearchToWeaponRoot = Settings.ExpandSearchToWeaponRoot.Value;
            target.ZoomMultiplier = 1f;
            target.CutWidthMultiplier = 1f;
            target.CutManual = false;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            _file = ReadFile(FilePath) ?? new ScopeMeshSurgerySettingsFile();
            _userKeys.Clear();

            var user = ReadFile(UserFilePath);
            int userCount = 0;
            if (user != null)
            {
                foreach (var entry in user.Entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.ScopeKey)) continue;
                    _file.Entries.RemoveAll(e => e != null && string.Equals(e.ScopeKey, entry.ScopeKey, StringComparison.OrdinalIgnoreCase));
                    _file.Entries.Add(entry);
                    _userKeys.Add(entry.ScopeKey);
                    userCount++;
                }
            }

            PiPDisablerPlugin.LogSource.LogInfo(
                $"[CustomMeshSettings] Loaded {_file.Entries.Count - userCount} bundled + {userCount} saved-by-you scope settings ({UserFilePath}).");
        }

        private static ScopeMeshSurgerySettingsFile ReadFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                var parsed = JsonConvert.DeserializeObject<ScopeMeshSurgerySettingsFile>(File.ReadAllText(path));
                return parsed != null && parsed.Entries != null ? parsed : null;
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogSource.LogError($"[CustomMeshSettings] Failed to read {path}: {ex.Message}");
                return null;
            }
        }

        internal static bool IsUserEdited(string scopeKey)
        {
            EnsureLoaded();
            return !string.IsNullOrWhiteSpace(scopeKey) && _userKeys.Contains(scopeKey);
        }

        private static void MarkUserEdited(string scopeKey)
        {
            if (!string.IsNullOrWhiteSpace(scopeKey))
                _userKeys.Add(scopeKey);
        }

        /// <summary>Writes only the player's own entries, to the config folder.</summary>
        private static void WriteToDisk()
        {
            try
            {
                var user = new ScopeMeshSurgerySettingsFile();
                foreach (var entry in _file.Entries)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.ScopeKey) && _userKeys.Contains(entry.ScopeKey))
                        user.Entries.Add(entry);
                }

                string path = UserFilePath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(user, Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogSource.LogError($"[CustomMeshSettings] Failed to save settings json: {ex.Message}");
            }
        }

        private static float GetPositiveOrDefault(float value, float defaultValue)
        {
            return value > 0f ? value : defaultValue;
        }

        private static float GetLiveVignetteValue(float liveValue, float savedValue)
        {
            return string.IsNullOrWhiteSpace(_activeScopeKey) ? savedValue : liveValue;
        }

        private static string GetPluginRootDirectory()
        {
            string pluginDir = null;
            pluginDir = Path.GetDirectoryName(typeof(PerScopeMeshSurgerySettings).Assembly.Location);
            return pluginDir;
        }
    }

}
