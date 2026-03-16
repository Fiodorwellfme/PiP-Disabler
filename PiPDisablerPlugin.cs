using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler
{
    [BepInPlugin("com.fiodor.pipdisabler", "PiP-Disabler", "0.1.0")]
    public sealed class PiPDisablerPlugin : BaseUnityPlugin
    {
        internal static PiPDisablerPlugin Instance;

        // --- Logging helpers ---
        internal static void LogInfo(string msg) { if (Instance != null && ModSettings.VerboseLogging != null && ModSettings.VerboseLogging.Value) Instance.Logger.LogInfo(msg); }
        internal static void LogWarn(string msg) { if (Instance != null && ModSettings.VerboseLogging != null && ModSettings.VerboseLogging.Value) Instance.Logger.LogWarning(msg); }
        internal static void LogError(string msg) { if (Instance != null && ModSettings.VerboseLogging != null && ModSettings.VerboseLogging.Value) Instance.Logger.LogError(msg); }
        internal static void LogVerbose(string msg)
        {
            if (Instance != null && ModSettings.VerboseLogging != null && ModSettings.VerboseLogging.Value)
                Instance.Logger.LogInfo("[V] " + msg);
        }

        internal static string GetPluginRootDirectory()
        {
            string pluginDir;
            try
            {
                pluginDir = System.IO.Path.GetDirectoryName(Instance != null ? Instance.Info.Location : null);
            }
            catch
            {
                pluginDir = null;
            }

            if (string.IsNullOrEmpty(pluginDir))
                pluginDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "plugins");

            return pluginDir;
        }

        /// <summary>
        /// Returns the main FPS camera via CameraClass.Instance.Camera.
        /// Unlike Camera.main (which does FindObjectWithTag every call and
        /// can flicker to optic/UI cameras during ADS transitions),
        /// CameraClass is EFT's own singleton and always points to the
        /// correct main FPS camera.  Falls back to Camera.main if
        /// CameraClass isn't initialized yet (menus, loading).
        /// </summary>
        internal static Camera GetMainCamera()
        {
            try
            {
                if (CameraClass.Exist)
                {
                    var cam = CameraClass.Instance.Camera;
                    if (cam != null) return cam;
                }
            }
            catch { }
            return Camera.main;
        }

        /// <summary>
        /// Returns the local player via GameWorld singleton.
        /// Shared helper — used by WeaponScalingPatch, ZeroingController, ScopeLifecycle.
        /// </summary>
        internal static Player GetLocalPlayer()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                return gw?.MainPlayer;
            }
            catch { return null; }
        }

        /// <summary>
        /// Returns the display viewport in pixels (accounts for DLSS/FSR).
        /// Shared helper — used by ReticleRenderer and ScopeEffectsRenderer.
        /// </summary>
        internal static Rect GetDisplayViewport(Camera cam)
        {
            float w = Mathf.Max(1f, Screen.width);
            float h = Mathf.Max(1f, Screen.height);
            if (cam != null)
            {
                w = Mathf.Max(w, cam.pixelWidth);
                h = Mathf.Max(h, cam.pixelHeight);
            }
            return new Rect(0f, 0f, w, h);
        }

        /// <summary>
        /// Check if two transforms share the same mode_XXX ancestor.
        /// Shared helper — used by FovController, CameraSettingsManager.
        /// </summary>
        internal static bool IsOnSameMode(Transform a, Transform b)
        {
            var mA = FindModeAncestor(a);
            var mB = FindModeAncestor(b);
            return mA == mB;
        }

        private static Transform FindModeAncestor(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name != null && p.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }
        private void Awake()
        {
            Instance = this;

            ModSettings.Bind(this);

            Patches.Patcher.Enable();

            // Initialize scope detection via PWA reflection
            ScopeLifecycle.Init();

            // --- Config change handlers (catches config manager changes, not just hotkeys) ---
            ModSettings.ModEnabled.SettingChanged += OnModEnabledChanged;
            ModSettings.EnableWeaponScaling.SettingChanged += OnWeaponScalingToggled;
            ModSettings.ScopeWhitelistNames.SettingChanged += OnWhitelistSettingsChanged;

            LogInfo("PiPDisabler v4.7.0 loaded.");
            LogInfo($"  ModEnabled={ModSettings.ModEnabled.Value}  DisablePiP={ModSettings.DisablePiP.Value}  MakeLensesTransparent={ModSettings.MakeLensesTransparent.Value}");
            LogInfo($"  WhitelistNames='{ModSettings.ScopeWhitelistNames.Value}'");
            LogInfo($"  EnableZoom={ModSettings.EnableZoom.Value}");
            LogInfo($"  AutoFov={ModSettings.AutoFovFromScope.Value}  DefaultZoom={ModSettings.DefaultZoom.Value}  FovAnimDur={ModSettings.FovAnimationDuration.Value}s");
            LogInfo($"  EnableMeshSurgery={ModSettings.EnableMeshSurgery.Value}  CutMode={ModSettings.CutMode.Value}  CutLen={ModSettings.CutLength.Value}  NearPreserve={ModSettings.NearPreserveDepth.Value}  ShowReticle={ModSettings.ShowReticle.Value}");
        }

        private static ScopeMeshSurgerySettingsEntry ActiveScopeOverride => PerScopeMeshSurgerySettings.GetActiveOverride();

        internal static float GetPlaneOffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneOffsetMeters : ModSettings.PlaneOffsetMeters.Value;
        internal static string GetPlaneNormalAxis() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneNormalAxis : ModSettings.PlaneNormalAxis.Value;
        internal static float GetCutRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.CutRadius : ModSettings.CutRadius.Value;
        internal static bool GetShowCutPlane() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowCutPlane : ModSettings.ShowCutPlane.Value;
        internal static bool GetShowCutVolume() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowCutVolume : ModSettings.ShowCutVolume.Value;
        internal static float GetCutVolumeOpacity() => ActiveScopeOverride != null ? ActiveScopeOverride.CutVolumeOpacity : ModSettings.CutVolumeOpacity.Value;
        internal static string GetCutMode() => ActiveScopeOverride != null ? ActiveScopeOverride.CutMode : ModSettings.CutMode.Value;
        internal static float GetCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.CylinderRadius : ModSettings.CylinderRadius.Value;
        internal static float GetMidCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.MidCylinderRadius : ModSettings.MidCylinderRadius.Value;
        internal static float GetMidCylinderPosition() => ActiveScopeOverride != null ? ActiveScopeOverride.MidCylinderPosition : ModSettings.MidCylinderPosition.Value;
        internal static float GetFarCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.FarCylinderRadius : ModSettings.FarCylinderRadius.Value;
        internal static float GetPlane1OffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane1OffsetMeters : ModSettings.Plane1OffsetMeters.Value;
        internal static float GetPlane2Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Position : ModSettings.Plane2Position.Value;
        internal static float GetPlane2PositionNormalized(float cutLength)
        {
            const float legacyReferenceCutLength = 0.755493f;
            float p2LegacyNormalized = Mathf.Clamp01(GetPlane2Position());
            float anchoredDepth = p2LegacyNormalized * legacyReferenceCutLength;
            return cutLength > 1e-5f ? Mathf.Clamp01(anchoredDepth / cutLength) : 0f;
        }
        internal static float GetPlane2Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Radius : ModSettings.Plane2Radius.Value;
        internal static float GetPlane3Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Position : ModSettings.Plane3Position.Value;
        internal static float GetPlane3Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Radius : ModSettings.Plane3Radius.Value;
        internal static float GetPlane4Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Position : ModSettings.Plane4Position.Value;
        internal static float GetPlane4Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Radius : ModSettings.Plane4Radius.Value;
        internal static float GetCutStartOffset() => ActiveScopeOverride != null ? ActiveScopeOverride.CutStartOffset : ModSettings.CutStartOffset.Value;
        internal static float GetCutLength() => ActiveScopeOverride != null ? ActiveScopeOverride.CutLength : ModSettings.CutLength.Value;
        internal static float GetNearPreserveDepth() => ActiveScopeOverride != null ? ActiveScopeOverride.NearPreserveDepth : ModSettings.NearPreserveDepth.Value;
        internal static bool GetShowReticle() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowReticle : ModSettings.ShowReticle.Value;
        internal static float GetReticleBaseSize() => ActiveScopeOverride != null ? ActiveScopeOverride.ReticleBaseSize : ModSettings.ReticleBaseSize.Value;
        internal static bool GetRestoreOnUnscope() => ActiveScopeOverride != null ? ActiveScopeOverride.RestoreOnUnscope : ModSettings.RestoreOnUnscope.Value;
        internal static bool GetExpandSearchToWeaponRoot() => ActiveScopeOverride != null ? ActiveScopeOverride.ExpandSearchToWeaponRoot : ModSettings.ExpandSearchToWeaponRoot.Value;
        internal static bool GetDebugShowHousingMask() => ModSettings.DebugShowHousingMask?.Value ?? false;
        internal static bool GetDebugLogCutCandidates() => ModSettings.DebugLogCutCandidates?.Value ?? false;
        internal static bool GetDebugReticleAfterEverything() => ModSettings.DebugReticleAfterEverything?.Value ?? false;
        internal static bool GetAutoSwitchReticleRenderForNvg() => ModSettings.AutoSwitchReticleRenderForNvg?.Value ?? false;

        private void OnDestroy()
        {
            // Plugin unload or game exit — restore everything
            ScopeLifecycle.ForceExit();
            LensTransparency.FullRestoreAll();
            MeshSurgeryManager.CleanupForShutdown();
            PiPDisabler.RestoreAllCameras();

            ModSettings.ModEnabled.SettingChanged -= OnModEnabledChanged;
            ModSettings.EnableWeaponScaling.SettingChanged -= OnWeaponScalingToggled;
            ModSettings.ScopeWhitelistNames.SettingChanged -= OnWhitelistSettingsChanged;
        }

        /// <summary>
        /// Handles ModEnabled changes from ANY source (config manager, hotkey, external).
        /// </summary>
        private static void OnModEnabledChanged(object sender, EventArgs e)
        {
            if (!ModSettings.ModEnabled.Value)
            {
                ScopeLifecycle.ForceExit();
                LensTransparency.FullRestoreAll(); // restore any lingering black lens materials
                PiPDisabler.RestoreAllCameras();
            }
            else
            {
                ScopeLifecycle.SyncState();
            }
        }

        /// <summary>
        /// Handles ModSettings.EnableWeaponScaling toggle mid-session.
        /// Restore immediately on disable; re-capture on enable while scoped.
        /// </summary>
        private static void OnWeaponScalingToggled(object sender, EventArgs e)
        {
            if (!ModSettings.EnableWeaponScaling.Value)
            {
                Patches.WeaponScalingPatch.RestoreScale();
            }
            else if (ScopeLifecycle.IsScoped)
            {
                Patches.WeaponScalingPatch.CaptureBaseState();
            }
        }

        private static void OnWhitelistSettingsChanged(object sender, EventArgs e)
        {
            if (!ModSettings.ModEnabled.Value) return;

            // Re-evaluate bypass state immediately while scoped.
            if (ScopeLifecycle.IsScoped)
            {
                ScopeLifecycle.ForceExit();
                ScopeLifecycle.SyncState();
            }
        }


        private void Update()
        {
            // --- Global mod toggle (always active, even when mod is OFF) ---
            if (ModSettings.ModToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModSettings.ModToggleKey.Value))
            {
                ModSettings.ModEnabled.Value = !ModSettings.ModEnabled.Value;
                LogInfo($"[Global] Mod {(ModSettings.ModEnabled.Value ? "ENABLED" : "DISABLED")}");
                // Cleanup/restore handled by OnModEnabledChanged via SettingChanged
            }

            // When mod is disabled, skip ALL per-frame logic
            if (!ModSettings.ModEnabled.Value) return;

            // --- Feature toggle keys ---
            if (InputProxy.GetKeyDown(ModSettings.MeshSurgeryToggleKey.Value))
            {
                ModSettings.EnableMeshSurgery.Value = !ModSettings.EnableMeshSurgery.Value;
                LogInfo($"Mesh surgery toggled: {ModSettings.EnableMeshSurgery.Value}");
                if (!ModSettings.EnableMeshSurgery.Value)
                    MeshSurgeryManager.RestoreAll();
            }

            if (InputProxy.GetKeyDown(ModSettings.DisablePiPToggleKey.Value))
            {
                ModSettings.DisablePiP.Value = !ModSettings.DisablePiP.Value;
                LogInfo($"Disable PiP toggled: {ModSettings.DisablePiP.Value}");
                if (!ModSettings.DisablePiP.Value)
                    PiPDisabler.RestoreAllCameras();
            }

            if (ModSettings.ScopeWhitelistToggleEntryKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModSettings.ScopeWhitelistToggleEntryKey.Value))
            {
                ScopeLifecycle.ToggleActiveScopeWhitelistEntry();
            }

            if (ModSettings.SaveCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModSettings.SaveCustomMeshSurgerySettingsKey.Value))
            {
                string scopeKey = ScopeLifecycle.GetActiveScopeWhitelistKey();
                if (string.IsNullOrWhiteSpace(scopeKey))
                {
                    LogWarn("[CustomMeshSettings] Save ignored: no active scope key");
                }
                else
                {
                    bool saved = PerScopeMeshSurgerySettings.SaveCustomSettingsForScope(scopeKey);
                    LogInfo(saved
                        ? $"[CustomMeshSettings] Saved custom settings for scope key '{scopeKey}'"
                        : "[CustomMeshSettings] Save failed");
                }
            }

            if (ModSettings.DeleteCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModSettings.DeleteCustomMeshSurgerySettingsKey.Value))
            {
                string scopeKey = ScopeLifecycle.GetActiveScopeWhitelistKey();
                if (string.IsNullOrWhiteSpace(scopeKey))
                {
                    LogWarn("[CustomMeshSettings] Delete ignored: no active scope key");
                }
                else
                {
                    bool removed = PerScopeMeshSurgerySettings.DeleteCustomSettingsForScope(scopeKey);
                    LogInfo(removed
                        ? $"[CustomMeshSettings] Deleted custom settings for scope key '{scopeKey}'"
                        : $"[CustomMeshSettings] No custom settings existed for scope key '{scopeKey}'");
                }
            }

            if (InputProxy.GetKeyDown(ModSettings.LensesTransparentToggleKey.Value))
            {
                ModSettings.MakeLensesTransparent.Value = !ModSettings.MakeLensesTransparent.Value;
                LogInfo($"Lens transparency toggled: {ModSettings.MakeLensesTransparent.Value}");
                if (!ModSettings.MakeLensesTransparent.Value)
                    LensTransparency.RestoreAll();
            }

            if (ModSettings.ZoomToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModSettings.ZoomToggleKey.Value))
            {
                ModSettings.EnableZoom.Value = !ModSettings.EnableZoom.Value;
                LogInfo($"Zoom toggled: {ModSettings.EnableZoom.Value}");
                if (!ModSettings.EnableZoom.Value)
                    ZoomController.Restore();
            }

            // --- Diagnostics dump ---
            if (ModSettings.DiagnosticsKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModSettings.DiagnosticsKey.Value))
                ScopeDiagnostics.Dump(ScopeLifecycle.ActiveOptic);

            // --- Per-frame logic ---
            PiPDisabler.TickBaseOpticCamera();

            // Safety-net: re-check scope state every frame in case we missed an event.
            ScopeLifecycle.CheckAndUpdate("Update");

            // Per-frame maintenance (ensure lens hidden, update variable zoom, etc.)
            ScopeLifecycle.Tick();
        }
        private static bool IsInRaid()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                return gw != null && gw.MainPlayer != null;
            }
            catch
            {
                return false;
            }
        }

        internal static string GetCurrentLocationId()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                return gw != null ? gw.LocationId : null;
            }
            catch
            {
                return null;
            }
        }

        internal static float GetManualLodBiasForCurrentMap()
        {
            float fallback = ModSettings.ManualLodBias != null ? ModSettings.ManualLodBias.Value : 0f;
            string locationId = GetCurrentLocationId();
            if (string.IsNullOrEmpty(locationId) || ModSettings.MapManualLodBias == null)
                return fallback;

            ConfigEntry<float> entry;
            if (ModSettings.MapManualLodBias.TryGetValue(locationId, out entry) && entry != null)
                return entry.Value;

            return fallback;
        }
        }

    internal static class InputProxy
    {
        private static System.Type _inputType;
        private static System.Reflection.MethodInfo _getKeyDown;
        private static System.Reflection.MethodInfo _getKey;
        private static System.Reflection.PropertyInfo _mouseScrollDelta;

        static InputProxy()
        {
            _inputType = System.Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule")
                      ?? System.Type.GetType("UnityEngine.Input, UnityEngine");
            if (_inputType != null)
            {
                _getKeyDown = _inputType.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                _getKey = _inputType.GetMethod("GetKey", new[] { typeof(KeyCode) });
                _mouseScrollDelta = _inputType.GetProperty("mouseScrollDelta",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            }
        }

        public static bool GetKeyDown(KeyCode key)
        {
            try
            {
                if (_getKeyDown == null) return false;
                return (bool)_getKeyDown.Invoke(null, new object[] { key });
            }
            catch { return false; }
        }

        /// <summary>Returns true while the key is held down.</summary>
        public static bool GetKey(KeyCode key)
        {
            try
            {
                if (_getKey == null) return false;
                return (bool)_getKey.Invoke(null, new object[] { key });
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns the Y component of Input.mouseScrollDelta.
        /// Positive = scroll up, negative = scroll down.
        /// </summary>
        public static float GetScrollDelta()
        {
            try
            {
                if (_mouseScrollDelta == null) return 0f;
                var vec = (Vector2)_mouseScrollDelta.GetValue(null);
                return vec.y;
            }
            catch { return 0f; }
        }

    }
}