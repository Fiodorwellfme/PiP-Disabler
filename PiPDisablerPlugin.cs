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
        internal static void LogInfo(string msg) { if (Instance != null && VerboseLogging != null && VerboseLogging.Value) Instance.Logger.LogInfo(msg); }
        internal static void LogWarn(string msg) { if (Instance != null && VerboseLogging != null && VerboseLogging.Value) Instance.Logger.LogWarning(msg); }
        internal static void LogError(string msg) { if (Instance != null && VerboseLogging != null && VerboseLogging.Value) Instance.Logger.LogError(msg); }
        internal static void LogVerbose(string msg)
        {
            if (Instance != null && VerboseLogging != null && VerboseLogging.Value)
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

        // Compatibility shims: keep existing PiPDisablerPlugin.* references working
        internal static ConfigEntry<bool> ModEnabled => ModSettings.ModEnabled;
        internal static ConfigEntry<KeyCode> ModToggleKey => ModSettings.ModToggleKey;
        internal static ConfigEntry<bool> AutoSwitchReticleRenderForNvg => ModSettings.AutoSwitchReticleRenderForNvg;
        internal static ConfigEntry<bool> DisablePiP => ModSettings.DisablePiP;
        internal static ConfigEntry<bool> AutoDisableForVariableScopes => ModSettings.AutoDisableForVariableScopes;
        internal static ConfigEntry<string> AutoBypassNameContains => ModSettings.AutoBypassNameContains;
        internal static ConfigEntry<string> ScopeWhitelistNames => ModSettings.ScopeWhitelistNames;
        internal static ConfigEntry<KeyCode> ScopeWhitelistToggleEntryKey => ModSettings.ScopeWhitelistToggleEntryKey;
        internal static ConfigEntry<KeyCode> DisablePiPToggleKey => ModSettings.DisablePiPToggleKey;
        internal static ConfigEntry<bool> MakeLensesTransparent => ModSettings.MakeLensesTransparent;
        internal static ConfigEntry<KeyCode> LensesTransparentToggleKey => ModSettings.LensesTransparentToggleKey;
        internal static ConfigEntry<bool> BlackLensWhenUnscoped => ModSettings.BlackLensWhenUnscoped;
        internal static ConfigEntry<bool> EnableMeshSurgery => ModSettings.EnableMeshSurgery;
        internal static ConfigEntry<KeyCode> MeshSurgeryToggleKey => ModSettings.MeshSurgeryToggleKey;
        internal static ConfigEntry<bool> RestoreOnUnscope => ModSettings.RestoreOnUnscope;
        internal static ConfigEntry<float> PlaneOffsetMeters => ModSettings.PlaneOffsetMeters;
        internal static ConfigEntry<string> PlaneNormalAxis => ModSettings.PlaneNormalAxis;
        internal static ConfigEntry<float> CutRadius => ModSettings.CutRadius;
        internal static ConfigEntry<bool> ShowCutPlane => ModSettings.ShowCutPlane;
        internal static ConfigEntry<bool> ShowCutVolume => ModSettings.ShowCutVolume;
        internal static ConfigEntry<float> CutVolumeOpacity => ModSettings.CutVolumeOpacity;
        internal static ConfigEntry<string> CutMode => ModSettings.CutMode;
        internal static ConfigEntry<float> CylinderRadius => ModSettings.CylinderRadius;
        internal static ConfigEntry<float> MidCylinderRadius => ModSettings.MidCylinderRadius;
        internal static ConfigEntry<float> MidCylinderPosition => ModSettings.MidCylinderPosition;
        internal static ConfigEntry<float> FarCylinderRadius => ModSettings.FarCylinderRadius;
        internal static ConfigEntry<float> Plane1OffsetMeters => ModSettings.Plane1OffsetMeters;
        internal static ConfigEntry<float> Plane2Position => ModSettings.Plane2Position;
        internal static ConfigEntry<float> Plane2Radius => ModSettings.Plane2Radius;
        internal static ConfigEntry<float> Plane3Position => ModSettings.Plane3Position;
        internal static ConfigEntry<float> Plane3Radius => ModSettings.Plane3Radius;
        internal static ConfigEntry<float> Plane4Position => ModSettings.Plane4Position;
        internal static ConfigEntry<float> Plane4Radius => ModSettings.Plane4Radius;
        internal static ConfigEntry<float> CutStartOffset => ModSettings.CutStartOffset;
        internal static ConfigEntry<float> CutLength => ModSettings.CutLength;
        internal static ConfigEntry<float> NearPreserveDepth => ModSettings.NearPreserveDepth;
        internal static ConfigEntry<bool> ShowReticle => ModSettings.ShowReticle;
        internal static ConfigEntry<float> ReticleBaseSize => ModSettings.ReticleBaseSize;
        internal static ConfigEntry<bool> ExpandSearchToWeaponRoot => ModSettings.ExpandSearchToWeaponRoot;
        internal static ConfigEntry<bool> DebugShowHousingMask => ModSettings.DebugShowHousingMask;
        internal static ConfigEntry<bool> StencilIncludeWeaponMeshes => ModSettings.StencilIncludeWeaponMeshes;
        internal static ConfigEntry<KeyCode> SaveCustomMeshSurgerySettingsKey => ModSettings.SaveCustomMeshSurgerySettingsKey;
        internal static ConfigEntry<KeyCode> DeleteCustomMeshSurgerySettingsKey => ModSettings.DeleteCustomMeshSurgerySettingsKey;
        internal static ConfigEntry<float> CustomPlaneOffsetMeters => ModSettings.CustomPlaneOffsetMeters;
        internal static ConfigEntry<string> CustomPlaneNormalAxis => ModSettings.CustomPlaneNormalAxis;
        internal static ConfigEntry<float> CustomCutRadius => ModSettings.CustomCutRadius;
        internal static ConfigEntry<bool> CustomShowCutPlane => ModSettings.CustomShowCutPlane;
        internal static ConfigEntry<bool> CustomShowCutVolume => ModSettings.CustomShowCutVolume;
        internal static ConfigEntry<float> CustomCutVolumeOpacity => ModSettings.CustomCutVolumeOpacity;
        internal static ConfigEntry<string> CustomCutMode => ModSettings.CustomCutMode;
        internal static ConfigEntry<float> CustomCylinderRadius => ModSettings.CustomCylinderRadius;
        internal static ConfigEntry<float> CustomMidCylinderRadius => ModSettings.CustomMidCylinderRadius;
        internal static ConfigEntry<float> CustomMidCylinderPosition => ModSettings.CustomMidCylinderPosition;
        internal static ConfigEntry<float> CustomFarCylinderRadius => ModSettings.CustomFarCylinderRadius;
        internal static ConfigEntry<float> CustomPlane1OffsetMeters => ModSettings.CustomPlane1OffsetMeters;
        internal static ConfigEntry<float> CustomPlane2Position => ModSettings.CustomPlane2Position;
        internal static ConfigEntry<float> CustomPlane2Radius => ModSettings.CustomPlane2Radius;
        internal static ConfigEntry<float> CustomPlane3Position => ModSettings.CustomPlane3Position;
        internal static ConfigEntry<float> CustomPlane3Radius => ModSettings.CustomPlane3Radius;
        internal static ConfigEntry<float> CustomPlane4Position => ModSettings.CustomPlane4Position;
        internal static ConfigEntry<float> CustomPlane4Radius => ModSettings.CustomPlane4Radius;
        internal static ConfigEntry<float> CustomCutStartOffset => ModSettings.CustomCutStartOffset;
        internal static ConfigEntry<float> CustomCutLength => ModSettings.CustomCutLength;
        internal static ConfigEntry<float> CustomNearPreserveDepth => ModSettings.CustomNearPreserveDepth;
        internal static ConfigEntry<bool> CustomShowReticle => ModSettings.CustomShowReticle;
        internal static ConfigEntry<float> CustomReticleBaseSize => ModSettings.CustomReticleBaseSize;
        internal static ConfigEntry<bool> CustomRestoreOnUnscope => ModSettings.CustomRestoreOnUnscope;
        internal static ConfigEntry<bool> CustomExpandSearchToWeaponRoot => ModSettings.CustomExpandSearchToWeaponRoot;
        internal static ConfigEntry<bool> VignetteEnabled => ModSettings.VignetteEnabled;
        internal static ConfigEntry<float> VignetteOpacity => ModSettings.VignetteOpacity;
        internal static ConfigEntry<float> VignetteSizeMult => ModSettings.VignetteSizeMult;
        internal static ConfigEntry<float> VignetteSoftness => ModSettings.VignetteSoftness;
        internal static ConfigEntry<bool> ScopeShadowEnabled => ModSettings.ScopeShadowEnabled;
        internal static ConfigEntry<float> ScopeShadowOpacity => ModSettings.ScopeShadowOpacity;
        internal static ConfigEntry<float> ScopeShadowRadius => ModSettings.ScopeShadowRadius;
        internal static ConfigEntry<float> ScopeShadowSoftness => ModSettings.ScopeShadowSoftness;
        internal static ConfigEntry<KeyCode> DiagnosticsKey => ModSettings.DiagnosticsKey;
        internal static ConfigEntry<bool> EnableWeaponScaling => ModSettings.EnableWeaponScaling;
        internal static ConfigEntry<float> BaselineWeaponScale => ModSettings.BaselineWeaponScale;
        internal static ConfigEntry<float> WeaponScaleStrength => ModSettings.WeaponScaleStrength;
        internal static ConfigEntry<bool> EnableZoom => ModSettings.EnableZoom;
        internal static ConfigEntry<float> DefaultZoom => ModSettings.DefaultZoom;
        internal static ConfigEntry<bool> AutoFovFromScope => ModSettings.AutoFovFromScope;
        internal static ConfigEntry<float> ScopedFov => ModSettings.ScopedFov;
        internal static ConfigEntry<float> FovAnimationDuration => ModSettings.FovAnimationDuration;
        internal static ConfigEntry<KeyCode> ZoomToggleKey => ModSettings.ZoomToggleKey;
        internal static ConfigEntry<float> ManualLodBias => ModSettings.ManualLodBias;
        internal static ConfigEntry<int> ManualMaximumLodLevel => ModSettings.ManualMaximumLodLevel;
        internal static ConfigEntry<float> ManualCullingMultiplier => ModSettings.ManualCullingMultiplier;
        internal static Dictionary<string, ConfigEntry<float>> MapManualLodBias => ModSettings.MapManualLodBias;
        internal static ConfigEntry<bool> EnableZeroing => ModSettings.EnableZeroing;
        internal static ConfigEntry<KeyCode> ZeroingUpKey => ModSettings.ZeroingUpKey;
        internal static ConfigEntry<KeyCode> ZeroingDownKey => ModSettings.ZeroingDownKey;
        internal static ConfigEntry<bool> VerboseLogging => ModSettings.VerboseLogging;
        internal static ConfigEntry<bool> DebugLogCutCandidates => ModSettings.DebugLogCutCandidates;
        internal static ConfigEntry<bool> DebugReticleAfterEverything => ModSettings.DebugReticleAfterEverything;

        private void Awake()
        {
            Instance = this;

            ModSettings.Bind(this);

            Patches.Patcher.Enable();

            // Initialize scope detection via PWA reflection
            ScopeLifecycle.Init();

            // Initialize freelook detection (Player.MouseLookControl)
            FreelookTracker.Init();

            // --- Config change handlers (catches config manager changes, not just hotkeys) ---
            ModEnabled.SettingChanged += OnModEnabledChanged;
            EnableWeaponScaling.SettingChanged += OnWeaponScalingToggled;
            ScopeWhitelistNames.SettingChanged += OnWhitelistSettingsChanged;

            LogInfo("PiPDisabler v4.7.0 loaded.");
            LogInfo($"  ModEnabled={ModEnabled.Value}  DisablePiP={DisablePiP.Value}  MakeLensesTransparent={MakeLensesTransparent.Value}");
            LogInfo($"  WhitelistNames='{ScopeWhitelistNames.Value}'");
            LogInfo($"  EnableZoom={EnableZoom.Value}");
            LogInfo($"  AutoFov={AutoFovFromScope.Value}  DefaultZoom={DefaultZoom.Value}  FovAnimDur={FovAnimationDuration.Value}s");
            LogInfo($"  EnableMeshSurgery={EnableMeshSurgery.Value}  CutMode={CutMode.Value}  CutLen={CutLength.Value}  NearPreserve={NearPreserveDepth.Value}  ShowReticle={ShowReticle.Value}");
        }

        private static ScopeMeshSurgerySettingsEntry ActiveScopeOverride => PerScopeMeshSurgerySettings.GetActiveOverride();

        internal static float GetPlaneOffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneOffsetMeters : PlaneOffsetMeters.Value;
        internal static string GetPlaneNormalAxis() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneNormalAxis : PlaneNormalAxis.Value;
        internal static float GetCutRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.CutRadius : CutRadius.Value;
        internal static bool GetShowCutPlane() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowCutPlane : ShowCutPlane.Value;
        internal static bool GetShowCutVolume() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowCutVolume : ShowCutVolume.Value;
        internal static float GetCutVolumeOpacity() => ActiveScopeOverride != null ? ActiveScopeOverride.CutVolumeOpacity : CutVolumeOpacity.Value;
        internal static string GetCutMode() => ActiveScopeOverride != null ? ActiveScopeOverride.CutMode : CutMode.Value;
        internal static float GetCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.CylinderRadius : CylinderRadius.Value;
        internal static float GetMidCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.MidCylinderRadius : MidCylinderRadius.Value;
        internal static float GetMidCylinderPosition() => ActiveScopeOverride != null ? ActiveScopeOverride.MidCylinderPosition : MidCylinderPosition.Value;
        internal static float GetFarCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.FarCylinderRadius : FarCylinderRadius.Value;
        internal static float GetPlane1OffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane1OffsetMeters : Plane1OffsetMeters.Value;
        internal static float GetPlane2Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Position : Plane2Position.Value;
        internal static float GetPlane2PositionNormalized(float cutLength)
        {
            const float legacyReferenceCutLength = 0.755493f;
            float p2LegacyNormalized = Mathf.Clamp01(GetPlane2Position());
            float anchoredDepth = p2LegacyNormalized * legacyReferenceCutLength;
            return cutLength > 1e-5f ? Mathf.Clamp01(anchoredDepth / cutLength) : 0f;
        }
        internal static float GetPlane2Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Radius : Plane2Radius.Value;
        internal static float GetPlane3Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Position : Plane3Position.Value;
        internal static float GetPlane3Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Radius : Plane3Radius.Value;
        internal static float GetPlane4Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Position : Plane4Position.Value;
        internal static float GetPlane4Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Radius : Plane4Radius.Value;
        internal static float GetCutStartOffset() => ActiveScopeOverride != null ? ActiveScopeOverride.CutStartOffset : CutStartOffset.Value;
        internal static float GetCutLength() => ActiveScopeOverride != null ? ActiveScopeOverride.CutLength : CutLength.Value;
        internal static float GetNearPreserveDepth() => ActiveScopeOverride != null ? ActiveScopeOverride.NearPreserveDepth : NearPreserveDepth.Value;
        internal static bool GetShowReticle() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowReticle : ShowReticle.Value;
        internal static float GetReticleBaseSize() => ActiveScopeOverride != null ? ActiveScopeOverride.ReticleBaseSize : ReticleBaseSize.Value;
        internal static bool GetRestoreOnUnscope() => ActiveScopeOverride != null ? ActiveScopeOverride.RestoreOnUnscope : RestoreOnUnscope.Value;
        internal static bool GetExpandSearchToWeaponRoot() => ActiveScopeOverride != null ? ActiveScopeOverride.ExpandSearchToWeaponRoot : ExpandSearchToWeaponRoot.Value;
        internal static bool GetDebugShowHousingMask() => DebugShowHousingMask?.Value ?? false;
        internal static bool GetDebugLogCutCandidates() => DebugLogCutCandidates?.Value ?? false;
        internal static bool GetDebugReticleAfterEverything() => DebugReticleAfterEverything?.Value ?? false;
        internal static bool GetAutoSwitchReticleRenderForNvg() => AutoSwitchReticleRenderForNvg?.Value ?? false;

        private void OnDestroy()
        {
            // Plugin unload or game exit — restore everything
            ScopeLifecycle.ForceExit();
            LensTransparency.FullRestoreAll();
            MeshSurgeryManager.CleanupForShutdown();
            PiPDisabler.RestoreAllCameras();

            ModEnabled.SettingChanged -= OnModEnabledChanged;
            EnableWeaponScaling.SettingChanged -= OnWeaponScalingToggled;
            ScopeWhitelistNames.SettingChanged -= OnWhitelistSettingsChanged;
        }

        /// <summary>
        /// Handles ModEnabled changes from ANY source (config manager, hotkey, external).
        /// </summary>
        private static void OnModEnabledChanged(object sender, EventArgs e)
        {
            if (!ModEnabled.Value)
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
        /// Handles EnableWeaponScaling toggle mid-session.
        /// Restore immediately on disable; re-capture on enable while scoped.
        /// </summary>
        private static void OnWeaponScalingToggled(object sender, EventArgs e)
        {
            if (!EnableWeaponScaling.Value)
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
            if (!ModEnabled.Value) return;

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
            if (ModToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModToggleKey.Value))
            {
                ModEnabled.Value = !ModEnabled.Value;
                LogInfo($"[Global] Mod {(ModEnabled.Value ? "ENABLED" : "DISABLED")}");
                // Cleanup/restore handled by OnModEnabledChanged via SettingChanged
            }

            // When mod is disabled, skip ALL per-frame logic
            if (!ModEnabled.Value) return;

            // --- Feature toggle keys ---
            if (InputProxy.GetKeyDown(MeshSurgeryToggleKey.Value))
            {
                EnableMeshSurgery.Value = !EnableMeshSurgery.Value;
                LogInfo($"Mesh surgery toggled: {EnableMeshSurgery.Value}");
                if (!EnableMeshSurgery.Value)
                    MeshSurgeryManager.RestoreAll();
            }

            if (InputProxy.GetKeyDown(DisablePiPToggleKey.Value))
            {
                DisablePiP.Value = !DisablePiP.Value;
                LogInfo($"Disable PiP toggled: {DisablePiP.Value}");
                if (!DisablePiP.Value)
                    PiPDisabler.RestoreAllCameras();
            }

            if (ScopeWhitelistToggleEntryKey.Value != KeyCode.None && InputProxy.GetKeyDown(ScopeWhitelistToggleEntryKey.Value))
            {
                ScopeLifecycle.ToggleActiveScopeWhitelistEntry();
            }

            if (SaveCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(SaveCustomMeshSurgerySettingsKey.Value))
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

            if (DeleteCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(DeleteCustomMeshSurgerySettingsKey.Value))
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

            if (InputProxy.GetKeyDown(LensesTransparentToggleKey.Value))
            {
                MakeLensesTransparent.Value = !MakeLensesTransparent.Value;
                LogInfo($"Lens transparency toggled: {MakeLensesTransparent.Value}");
                if (!MakeLensesTransparent.Value)
                    LensTransparency.RestoreAll();
            }

            if (ZoomToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ZoomToggleKey.Value))
            {
                EnableZoom.Value = !EnableZoom.Value;
                LogInfo($"Zoom toggled: {EnableZoom.Value}");
                if (!EnableZoom.Value)
                    ZoomController.Restore();
            }

            // --- Diagnostics dump ---
            if (DiagnosticsKey.Value != KeyCode.None && InputProxy.GetKeyDown(DiagnosticsKey.Value))
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
            float fallback = ManualLodBias != null ? ManualLodBias.Value : 0f;
            string locationId = GetCurrentLocationId();
            if (string.IsNullOrEmpty(locationId) || MapManualLodBias == null)
                return fallback;

            ConfigEntry<float> entry;
            if (MapManualLodBias.TryGetValue(locationId, out entry) && entry != null)
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
