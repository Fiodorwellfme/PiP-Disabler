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

        // --- 0. Global ---
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<KeyCode> ModToggleKey;
        internal const bool AutoSwitchReticleRenderForNvg = true;

        // --- General ---
        internal const bool DisablePiP = true;
        internal const bool AutoDisableForVariableScopes = true;
        internal const string AutoBypassNameContains = "npz, PU, vomz, d-evo";
        internal static ConfigEntry<string> ScopeWhitelistNames;
        internal static ConfigEntry<KeyCode> ScopeWhitelistToggleEntryKey;
        internal const bool MakeLensesTransparent = true;
        internal const bool BlackLensWhenUnscoped = true;

        // --- Mesh Surgery ---
        internal const bool EnableMeshSurgery = true;
        internal const bool RestoreOnUnscope = true;
        private const float PlaneOffsetMeters = 0.001f;
        private const string PlaneNormalAxis = "-Y";
        private const float CutRadius = 0f;
        private const bool ShowCutPlane = false;
        private const bool ShowCutVolume = false;
        private const float CutVolumeOpacity = 0.49f;
        private const string CutMode = "Cylinder";
        private const float CylinderRadius = 0.011f;
        private const float MidCylinderRadius = 0.013f;
        private const float MidCylinderPosition = 0.28f;
        private const float FarCylinderRadius = 0.12f;
        private const float Plane1OffsetMeters = 0f;
        private const float Plane2Position = 0.1138498f;
        private const float Plane2Radius = 0.0186338f;
        private const float Plane3Position = 0.55f;
        private const float Plane3Radius = 0.2f;
        private const float Plane4Position = 1f;
        private const float Plane4Radius = 0.2f;
        private const float CutStartOffset = 0.04084507f;
        private const float CutLength = 0.755493f;
        private const float NearPreserveDepth = 0.02549295f;
        private const bool ShowReticle = true;
        private const float ReticleBaseSize = 0.03f;
        internal const bool ExpandSearchToWeaponRoot = true;
        internal const bool DebugShowHousingMask = false;
        internal const bool StencilIncludeWeaponMeshes = true;

        // --- Custom Mesh Surgery settings (per-scope authoring) ---
        internal static ConfigEntry<KeyCode> SaveCustomMeshSurgerySettingsKey;
        internal static ConfigEntry<KeyCode> DeleteCustomMeshSurgerySettingsKey;
        internal static ConfigEntry<float> CustomPlaneOffsetMeters;
        internal static ConfigEntry<string> CustomPlaneNormalAxis;
        internal static ConfigEntry<float> CustomCutRadius;
        internal static ConfigEntry<bool> CustomShowCutPlane;
        internal static ConfigEntry<bool> CustomShowCutVolume;
        internal static ConfigEntry<float> CustomCutVolumeOpacity;
        internal static ConfigEntry<string> CustomCutMode;
        internal static ConfigEntry<float> CustomCylinderRadius;
        internal static ConfigEntry<float> CustomMidCylinderRadius;
        internal static ConfigEntry<float> CustomMidCylinderPosition;
        internal static ConfigEntry<float> CustomFarCylinderRadius;
        internal static ConfigEntry<float> CustomPlane1OffsetMeters;
        internal static ConfigEntry<float> CustomPlane2Position;
        internal static ConfigEntry<float> CustomPlane2Radius;
        internal static ConfigEntry<float> CustomPlane3Position;
        internal static ConfigEntry<float> CustomPlane3Radius;
        internal static ConfigEntry<float> CustomPlane4Position;
        internal static ConfigEntry<float> CustomPlane4Radius;
        internal static ConfigEntry<float> CustomCutStartOffset;
        internal static ConfigEntry<float> CustomCutLength;
        internal static ConfigEntry<float> CustomNearPreserveDepth;
        internal static ConfigEntry<bool> CustomShowReticle;
        internal static ConfigEntry<float> CustomReticleBaseSize;
        internal static ConfigEntry<bool> CustomRestoreOnUnscope;
        internal static ConfigEntry<bool> CustomExpandSearchToWeaponRoot;

        // --- Scope Effects ---
        internal static ConfigEntry<bool>  VignetteEnabled;
        internal static ConfigEntry<float> VignetteOpacity;
        internal static ConfigEntry<float> VignetteSizeMult;
        internal static ConfigEntry<float> VignetteSoftness;
        internal static ConfigEntry<bool>  ScopeShadowEnabled;
        internal static ConfigEntry<float> ScopeShadowOpacity;
        internal static ConfigEntry<float> ScopeShadowRadius;
        internal static ConfigEntry<float> ScopeShadowSoftness;

        // --- Diagnostics ---
        internal static ConfigEntry<KeyCode> DiagnosticsKey;

        // --- Weapon Scaling ---
        internal const bool EnableWeaponScaling = true;
        internal const float BaselineWeaponScale = 0.9624413f;
        internal const float WeaponScaleStrength = 0.2723005f;
        // --- Zoom / FOV ---
        internal const bool EnableZoom = true;
        internal const float DefaultZoom = 4f;
        internal const bool AutoFovFromScope = true;
        internal const float ScopedFov = 15f;
        internal static ConfigEntry<float> FovAnimationDuration;
        internal const float ManualLodBias = 4.0f;
        internal const int ManualMaximumLodLevel = -1;
        internal const float ManualCullingMultiplier = 0.8f;

        // --- 4. Zeroing ---
        internal const bool EnableZeroing = true;
        internal const KeyCode ZeroingUpKey = KeyCode.PageUp;
        internal const KeyCode ZeroingDownKey = KeyCode.PageDown;

        // --- Debug ---
        internal static ConfigEntry<bool> VerboseLogging;
        internal const bool DebugLogCutCandidates = false;
        internal const bool DebugReticleAfterEverything = false;


        private void Awake()
        {
            Instance = this;

            // --- 0. Global ---
            ModEnabled = Config.Bind("1. General", "ModEnabled", true,
                "Master ON/OFF switch for the entire mod. When OFF, all effects are " +
                "cleaned up and the game behaves as if the mod is not installed.");
            ModToggleKey = Config.Bind("1. General", "ModToggleKey", KeyCode.Backspace,
                "Toggle key for master mod enable/disable.");

            // --- General ---
            ScopeWhitelistNames = Config.Bind("2. Whitelist", "ScopeWhitelistNames", "",
                "Comma/semicolon/newline separated list of allowed scope keys.\n" +
                "Primary key is derived from the object under mod_scope that does not contain mount (case-insensitive).\n" +
                "Fallbacks: template _name, template _id, then optic object name. Empty list = whitelist ignored.");
            ScopeWhitelistToggleEntryKey = Config.Bind("2. Whitelist", "ScopeWhitelistToggleEntryKey", KeyCode.None,
                "When pressed while scoped, add/remove the current scope key in ScopeWhitelistNames (derived from mod_scope non-mount object).");

            // --- Weapon Scaling ---

            // --- Zoom ---
            FovAnimationDuration = Config.Bind("1. General", "FovAnimationDuration", 1f,
                new ConfigDescription(
                    "Duration (seconds) of the FOV zoom-in animation when entering ADS.\n" +
                    "0 = instant snap. 0.25 = smooth quarter-second transition.\n" +
                    "Scope exit always restores FOV instantly to avoid sluggish feel.",
                    new AcceptableValueRange<float>(0f, 2f)));

            // --- 4. Zeroing ---

            // --- Mesh Surgery (ON by default, Cylinder mode) ---

            // --- Custom Mesh Surgery settings ---
            SaveCustomMeshSurgerySettingsKey = Config.Bind("5. Custom mesh surgery settings", "SaveCustomMeshSurgerySettingsKey", KeyCode.None,
                "When pressed while scoped, save all values from this category for the active scope key into custom_mesh_surgery_settings.json.");
            DeleteCustomMeshSurgerySettingsKey = Config.Bind("5. Custom mesh surgery settings", "DeleteCustomMeshSurgerySettingsKey", KeyCode.None,
                "When pressed while scoped, delete custom mesh surgery settings for the active scope key from custom_mesh_surgery_settings.json.");
            CustomPlaneOffsetMeters = Config.Bind("5. Custom mesh surgery settings", "PlaneOffsetMeters", 0.001f, "Custom per-scope plane offset applied along plane normal (meters).");
            CustomPlaneNormalAxis = Config.Bind("5. Custom mesh surgery settings", "PlaneNormalAxis", "-Y", new ConfigDescription("Custom per-scope local axis for the cut plane normal.", new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z")));
            CustomCutRadius = Config.Bind("5. Custom mesh surgery settings", "CutRadius", 0f, new ConfigDescription("Custom per-scope max cut distance in meters (0 = unlimited).", new AcceptableValueRange<float>(0f, 1f)));
            CustomShowCutPlane = Config.Bind("5. Custom mesh surgery settings", "ShowCutPlane", false, "Custom per-scope cut plane visualizer toggle.");
            CustomShowCutVolume = Config.Bind("5. Custom mesh surgery settings", "ShowCutVolume", false, "Custom per-scope cut volume visualizer toggle.");
            CustomCutVolumeOpacity = Config.Bind("5. Custom mesh surgery settings", "CutVolumeOpacity", 0.49f, new ConfigDescription("Custom per-scope cut volume opacity.", new AcceptableValueRange<float>(0.05f, 0.8f)));
            CustomCutMode = Config.Bind("5. Custom mesh surgery settings", "CutMode", "Cylinder", new ConfigDescription("Custom per-scope mesh cut mode.", new AcceptableValueList<string>("Plane", "Cylinder")));
            CustomCylinderRadius = Config.Bind("5. Custom mesh surgery settings", "CylinderRadius", 0.011f, new ConfigDescription("Custom per-scope near radius in meters.", new AcceptableValueRange<float>(0.001f, 0.1f)));
            CustomMidCylinderRadius = Config.Bind("5. Custom mesh surgery settings", "MidCylinderRadius", 0.013f, new ConfigDescription("Custom per-scope mid profile radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomMidCylinderPosition = Config.Bind("5. Custom mesh surgery settings", "MidCylinderPosition", 0.28f, new ConfigDescription("Custom per-scope mid profile position (0..1).", new AcceptableValueRange<float>(0.01f, 0.99f)));
            CustomFarCylinderRadius = Config.Bind("5. Custom mesh surgery settings", "FarCylinderRadius", 0.12f, new ConfigDescription("Custom per-scope far radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomPlane1OffsetMeters = Config.Bind("5. Custom mesh surgery settings", "Plane1OffsetMeters", 0f, new ConfigDescription("Custom per-scope plane 1 offset in meters.", new AcceptableValueRange<float>(-0.02f, 0.02f)));
            CustomPlane2Position = Config.Bind("5. Custom mesh surgery settings", "Plane2Position", 0.1138498f, new ConfigDescription("Custom per-scope plane 2 position (0..1), anchored from near when CutLength changes.", new AcceptableValueRange<float>(0f, 1f)));
            CustomPlane2Radius = Config.Bind("5. Custom mesh surgery settings", "Plane2Radius", 0.0186338f, new ConfigDescription("Custom per-scope plane 2 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomPlane3Position = Config.Bind("5. Custom mesh surgery settings", "Plane3Position", 0.55f, new ConfigDescription("Custom per-scope plane 3 depth (0..1).", new AcceptableValueRange<float>(0f, 1f)));
            CustomPlane3Radius = Config.Bind("5. Custom mesh surgery settings", "Plane3Radius", 0.2f, new ConfigDescription("Custom per-scope plane 3 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomPlane4Position = Config.Bind("5. Custom mesh surgery settings", "Plane4Position", 1f, new ConfigDescription("Custom per-scope plane 4 depth (0..1).", new AcceptableValueRange<float>(0f, 1f)));
            CustomPlane4Radius = Config.Bind("5. Custom mesh surgery settings", "Plane4Radius", 0.2f, new ConfigDescription("Custom per-scope plane 4 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomCutStartOffset = Config.Bind("5. Custom mesh surgery settings", "CutStartOffset", 0.04084507f, new ConfigDescription("Custom per-scope cut start offset in meters.", new AcceptableValueRange<float>(-0.2f, 0.2f)));
            CustomCutLength = Config.Bind("5. Custom mesh surgery settings", "CutLength", 0.755493f, new ConfigDescription("Custom per-scope cut length in meters.", new AcceptableValueRange<float>(0.01f, 4f)));
            CustomNearPreserveDepth = Config.Bind("5. Custom mesh surgery settings", "NearPreserveDepth", 0.02549295f, new ConfigDescription("Custom per-scope near preserve depth in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomShowReticle = Config.Bind("5. Custom mesh surgery settings", "ShowReticle", true, "Custom per-scope reticle visibility.");
            CustomReticleBaseSize = Config.Bind("5. Custom mesh surgery settings", "ReticleBaseSize", 0.030f, new ConfigDescription("Custom per-scope reticle base diameter in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomRestoreOnUnscope = Config.Bind("5. Custom mesh surgery settings", "RestoreOnUnscope", true, "Custom per-scope restore behavior when leaving scope.");
            CustomExpandSearchToWeaponRoot = Config.Bind("5. Custom mesh surgery settings", "ExpandSearchToWeaponRoot", true, "Custom per-scope search root expansion to Weapon_root.");

            // --- Scope Effects ---
            VignetteEnabled = Config.Bind("3. Scope effects", "VignetteEnabled", true,
                "Render a circular vignette ring around the scope aperture.\n" +
                "A world-space quad at the lens position fading from transparent centre to black edge.");
            VignetteOpacity = Config.Bind("3. Scope effects", "VignetteOpacity", 0.39f,
                new ConfigDescription("Maximum opacity of the lens vignette ring (0=invisible, 1=full black).",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSizeMult = Config.Bind("3. Scope effects", "VignetteSizeMult", 0.35f,
                new ConfigDescription(
                    "Vignette quad diameter as a multiplier of ReticleBaseSize.\n" +
                    "1.0 = same size as reticle.  1.5 gives a visible border ring.\n" +
                    "Higher values (5-15) may be needed for high-magnification scopes.",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSoftness = Config.Bind("3. Scope effects", "VignetteSoftness", 0.51f,
                new ConfigDescription(
                    "Fraction of the vignette radius used for the gradient falloff (0=hard edge, 1=full gradient).",
                    new AcceptableValueRange<float>(0f, 1f)));

            ScopeShadowEnabled = Config.Bind("3. Scope effects", "ScopeShadowEnabled", true,
                "Overlay a fullscreen scope-tube shadow: black everywhere except a transparent\n" +
                "circular window in the centre.  Simulates looking down a scope tube.");
            ScopeShadowOpacity = Config.Bind("3. Scope effects", "ScopeShadowOpacity", 0.75f,
                new ConfigDescription("Maximum opacity of the scope shadow overlay.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ScopeShadowRadius = Config.Bind("3. Scope effects", "ScopeShadowRadius", 0.07859156f,
                new ConfigDescription(
                    "Radius of the transparent centre window as a fraction of the half-screen (0.0-0.5).\n" +
                    "0.18 = window fills roughly 36% of screen width.  Increase for wider aperture.",
                    new AcceptableValueRange<float>(0.02f, 0.5f)));
            ScopeShadowSoftness = Config.Bind("3. Scope effects", "ScopeShadowSoftness", 0.08535211f,
                new ConfigDescription(
                    "Width of the gradient edge between the clear window and the black shadow (fraction of screen).\n" +
                    "0 = hard edge.  0.05-0.1 is a natural-looking falloff.",
                    new AcceptableValueRange<float>(0f, 0.3f)));

            // --- Diagnostics ---
            DiagnosticsKey = Config.Bind("4. Debug", "DiagnosticsKey", KeyCode.None,
                "Press to log full diagnostics for the currently active scope: name, hierarchy,\n" +
                "magnification and cut-plane config.");

            // --- Debug ---
            VerboseLogging = Config.Bind("4. Debug", "VerboseLogging", false,
                "Enable detailed logging. Turn on to diagnose lens/zoom issues.");

            Patches.Patcher.Enable();

            // Initialize scope detection via PWA reflection
            ScopeLifecycle.Init();

            // Initialize freelook detection (Player.MouseLookControl)
            FreelookTracker.Init();

            // --- Config change handlers (catches config manager changes, not just hotkeys) ---
            ModEnabled.SettingChanged += OnModEnabledChanged;
            ScopeWhitelistNames.SettingChanged += OnWhitelistSettingsChanged;

            LogInfo("PiPDisabler v4.7.0 loaded.");
            LogInfo($"  ModEnabled={ModEnabled.Value}  DisablePiP={DisablePiP}  MakeLensesTransparent={MakeLensesTransparent}");
            LogInfo($"  WhitelistNames='{ScopeWhitelistNames.Value}'");
            LogInfo($"  EnableZoom={EnableZoom}");
            LogInfo($"  AutoFov={AutoFovFromScope}  DefaultZoom={DefaultZoom}  FovAnimDur={FovAnimationDuration.Value}s");
            LogInfo($"  EnableMeshSurgery={EnableMeshSurgery}  CutMode={CutMode}  CutLen={CutLength}  NearPreserve={NearPreserveDepth}  ShowReticle={ShowReticle}");
        }

        private static ScopeMeshSurgerySettingsEntry ActiveScopeOverride => PerScopeMeshSurgerySettings.GetActiveOverride();

        internal static float GetPlaneOffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneOffsetMeters : PlaneOffsetMeters;
        internal static string GetPlaneNormalAxis() => ActiveScopeOverride != null ? ActiveScopeOverride.PlaneNormalAxis : PlaneNormalAxis;
        internal static float GetCutRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.CutRadius : CutRadius;
        internal static bool GetShowCutPlane() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowCutPlane : ShowCutPlane;
        internal static bool GetShowCutVolume() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowCutVolume : ShowCutVolume;
        internal static float GetCutVolumeOpacity() => ActiveScopeOverride != null ? ActiveScopeOverride.CutVolumeOpacity : CutVolumeOpacity;
        internal static string GetCutMode() => ActiveScopeOverride != null ? ActiveScopeOverride.CutMode : CutMode;
        internal static float GetCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.CylinderRadius : CylinderRadius;
        internal static float GetMidCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.MidCylinderRadius : MidCylinderRadius;
        internal static float GetMidCylinderPosition() => ActiveScopeOverride != null ? ActiveScopeOverride.MidCylinderPosition : MidCylinderPosition;
        internal static float GetFarCylinderRadius() => ActiveScopeOverride != null ? ActiveScopeOverride.FarCylinderRadius : FarCylinderRadius;
        internal static float GetPlane1OffsetMeters() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane1OffsetMeters : Plane1OffsetMeters;
        internal static float GetPlane2Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Position : Plane2Position;
        internal static float GetPlane2PositionNormalized(float cutLength)
        {
            const float legacyReferenceCutLength = 0.755493f;
            float p2LegacyNormalized = Mathf.Clamp01(GetPlane2Position());
            float anchoredDepth = p2LegacyNormalized * legacyReferenceCutLength;
            return cutLength > 1e-5f ? Mathf.Clamp01(anchoredDepth / cutLength) : 0f;
        }
        internal static float GetPlane2Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane2Radius : Plane2Radius;
        internal static float GetPlane3Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Position : Plane3Position;
        internal static float GetPlane3Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane3Radius : Plane3Radius;
        internal static float GetPlane4Position() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Position : Plane4Position;
        internal static float GetPlane4Radius() => ActiveScopeOverride != null ? ActiveScopeOverride.Plane4Radius : Plane4Radius;
        internal static float GetCutStartOffset() => ActiveScopeOverride != null ? ActiveScopeOverride.CutStartOffset : CutStartOffset;
        internal static float GetCutLength() => ActiveScopeOverride != null ? ActiveScopeOverride.CutLength : CutLength;
        internal static float GetNearPreserveDepth() => ActiveScopeOverride != null ? ActiveScopeOverride.NearPreserveDepth : NearPreserveDepth;
        internal static bool GetShowReticle() => ActiveScopeOverride != null ? ActiveScopeOverride.ShowReticle : ShowReticle;
        internal static float GetReticleBaseSize() => ActiveScopeOverride != null ? ActiveScopeOverride.ReticleBaseSize : ReticleBaseSize;
        internal static bool GetRestoreOnUnscope() => ActiveScopeOverride != null ? ActiveScopeOverride.RestoreOnUnscope : RestoreOnUnscope;
        internal static bool GetExpandSearchToWeaponRoot() => ActiveScopeOverride != null ? ActiveScopeOverride.ExpandSearchToWeaponRoot : ExpandSearchToWeaponRoot;
        internal static bool GetDebugShowHousingMask() => DebugShowHousingMask;
        internal static bool GetDebugLogCutCandidates() => DebugLogCutCandidates;
        internal static bool GetDebugReticleAfterEverything() => DebugReticleAfterEverything;
        internal static bool GetAutoSwitchReticleRenderForNvg() => AutoSwitchReticleRenderForNvg;

        private void OnDestroy()
        {
            // Plugin unload or game exit — restore everything
            ScopeLifecycle.ForceExit();
            LensTransparency.FullRestoreAll();
            MeshSurgeryManager.CleanupForShutdown();
            PiPDisabler.RestoreAllCameras();

            ModEnabled.SettingChanged -= OnModEnabledChanged;
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
            return ManualLodBias;
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
