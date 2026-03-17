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
        internal static ConfigEntry<bool> AutoSwitchReticleRenderForNvg;

        // --- General ---
        internal static ConfigEntry<bool> DisablePiP;
        internal static ConfigEntry<bool> AutoDisableForVariableScopes;
        internal static ConfigEntry<string> AutoBypassNameContains;
        internal static ConfigEntry<string> ScopeWhitelistNames;
        internal static ConfigEntry<KeyCode> ScopeWhitelistToggleEntryKey;
        internal static ConfigEntry<KeyCode> DisablePiPToggleKey;
        internal static ConfigEntry<bool> MakeLensesTransparent;
        internal static ConfigEntry<KeyCode> LensesTransparentToggleKey;
        internal static ConfigEntry<bool> BlackLensWhenUnscoped;

        // --- Mesh Surgery ---
        internal static ConfigEntry<bool> EnableMeshSurgery;
        internal static ConfigEntry<KeyCode> MeshSurgeryToggleKey;
        internal static ConfigEntry<bool> RestoreOnUnscope;
        internal static ConfigEntry<float> PlaneOffsetMeters;
        internal static ConfigEntry<string> PlaneNormalAxis;
        internal static ConfigEntry<float> CutRadius;
        internal static ConfigEntry<bool> ShowCutPlane;
        internal static ConfigEntry<bool> ShowCutVolume;
        internal static ConfigEntry<float> CutVolumeOpacity;
        internal static ConfigEntry<string> CutMode;
        internal static ConfigEntry<float> CylinderRadius;
        internal static ConfigEntry<float> MidCylinderRadius;
        internal static ConfigEntry<float> MidCylinderPosition;
        internal static ConfigEntry<float> FarCylinderRadius;
        internal static ConfigEntry<float> Plane1OffsetMeters;
        internal static ConfigEntry<float> Plane2Position;
        internal static ConfigEntry<float> Plane2Radius;
        internal static ConfigEntry<float> Plane3Position;
        internal static ConfigEntry<float> Plane3Radius;
        internal static ConfigEntry<float> Plane4Position;
        internal static ConfigEntry<float> Plane4Radius;
        internal static ConfigEntry<float> CutStartOffset;
        internal static ConfigEntry<float> CutLength;
        internal static ConfigEntry<float> NearPreserveDepth;
        internal static ConfigEntry<bool> ShowReticle;
        internal static ConfigEntry<float> ReticleBaseSize;
        internal static ConfigEntry<bool> ExpandSearchToWeaponRoot;
        internal static ConfigEntry<bool> DebugShowHousingMask;
        internal static ConfigEntry<bool> StencilIncludeWeaponMeshes;

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
        internal static ConfigEntry<bool> EnableWeaponScaling;
        internal static ConfigEntry<float> BaselineWeaponScale;
        internal static ConfigEntry<float> WeaponScaleStrength;
        // --- Zoom / FOV ---
        internal static ConfigEntry<bool> EnableZoom;
        internal static ConfigEntry<float> DefaultZoom;
        internal static ConfigEntry<bool> AutoFovFromScope;
        internal static ConfigEntry<float> ScopedFov;
        internal static ConfigEntry<float> FovAnimationDuration;
        internal static ConfigEntry<KeyCode> ZoomToggleKey;
        internal static ConfigEntry<float> ManualLodBias;
        internal static ConfigEntry<int> ManualMaximumLodLevel;
        internal static ConfigEntry<float> ManualCullingMultiplier;
        internal static Dictionary<string, ConfigEntry<float>> MapManualLodBias;

        // --- 4. Zeroing ---
        internal static ConfigEntry<bool> EnableZeroing;
        internal static ConfigEntry<KeyCode> ZeroingUpKey;
        internal static ConfigEntry<KeyCode> ZeroingDownKey;

        // --- Debug ---
        internal static ConfigEntry<bool> VerboseLogging;
        internal static ConfigEntry<bool> DebugLogCutCandidates;
        internal static ConfigEntry<bool> DebugReticleAfterEverything;


        private void Awake()
        {
            Instance = this;

            // --- 0. Global ---
            ModEnabled = Config.Bind("Global", "ModEnabled", true,
                new ConfigDescription(
                    "Master ON/OFF switch for the entire mod.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ModToggleKey = Config.Bind("Global", "ModToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for master mod enable/disable.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            AutoSwitchReticleRenderForNvg = Config.Bind("General", "AutoSwitchReticleRenderForNvg", true,
                new ConfigDescription(
                    "Automatically switch reticle CommandBuffer event based on NVG state.\n" +
                "ON: use AfterForwardAlpha while NVG are active and AfterEverything otherwise.\n" +
                "OFF: keep the normal path (AfterForwardAlpha) unless debug override is enabled.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- General ---
            DisablePiP = Config.Bind("General", "DisablePiP", true,
                new ConfigDescription(
                    "Disable Picture-in-Picture optic rendering (No-PiP mode). " +
                "Core feature — gives identical perf between hip-fire and ADS.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoDisableForVariableScopes = Config.Bind("General", "AutoDisable", true,
                new ConfigDescription(
                    "Automatically disable all mod effects while scoped with unsupported scopes",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            AutoBypassNameContains = Config.Bind("General", "AutoBypassNameContains", "npz, PU, vomz, d-evo",
                new ConfigDescription(
                    "Comma-separated list of substrings. Any scope whose object name or scope key contains one of these ",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeWhitelistNames = Config.Bind("General", "ScopeWhitelistNames", "",
                new ConfigDescription(
                    "Comma/semicolon/newline separated list of allowed scope keys.\n" +
                "Primary key is derived from the object under mod_scope that does not contain mount (case-insensitive).\n" +
                "Fallbacks: template _name, template _id, then optic object name. Empty list = whitelist ignored.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeWhitelistToggleEntryKey = Config.Bind("General", "ScopeWhitelistToggleEntryKey", KeyCode.None,
                new ConfigDescription(
                    "When pressed while scoped, add/remove the current scope key in ScopeWhitelistNames (derived from mod_scope non-mount object).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            DisablePiPToggleKey = Config.Bind("General", "DisablePiPToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for PiP disable.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            MakeLensesTransparent = Config.Bind("General", "MakeLensesTransparent", true,
                new ConfigDescription(
                    "Hide lens surfaces (linza/backLens) while scoped so you see through the tube.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            LensesTransparentToggleKey = Config.Bind("General", "LensesTransparentToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for lens transparency.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BlackLensWhenUnscoped = Config.Bind("General", "BlackLensWhenUnscoped", true,
                new ConfigDescription(
                    "When unscoping, apply a solid black opaque material to the lens instead of restoring " +
                "the original PiP/sight material. Eliminates the reticle flash during the unscope " +
                "transition and gives the scope a realistic dark-glass appearance when not in use.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Weapon Scaling ---
            EnableWeaponScaling = Config.Bind("General", "EnableWeaponScaling", true,
                new ConfigDescription(
                    "Compensate weapon/arms model scale across magnification levels.\n" +
                "Without this, zooming in (lower FOV) makes the weapon appear larger on screen.\n" +
                "With this enabled, the weapon shrinks proportionally as you zoom in so it\n" +
                "always occupies the same screen space at every magnification level.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BaselineWeaponScale = Config.Bind("General", "BaselineWeaponScale", 0.9624413f,
                new ConfigDescription(
                    "Base weapon scale applied at all FOV values before compensation.\n" +
                    "1.00 = default EFT visual scale.",
                    new AcceptableValueRange<float>(0.00f, 2.00f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            WeaponScaleStrength = Config.Bind("General", "WeaponScaleStrength", 0.2723005f,
                new ConfigDescription(
                    "Blends between no compensation and full inverse-FOV compensation.\n" +
                    "0.00 = no compensation, 1.00 = full compensation, values outside [0,1] over/under-compensate.",
                    new AcceptableValueRange<float>(-2.00f, 2.00f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Zoom ---
            EnableZoom = Config.Bind("General", "EnableZoom", true,
                new ConfigDescription(
                    "Enable scope magnification via FOV zoom.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            DefaultZoom = Config.Bind("General", "DefaultZoom", 4f,
                new ConfigDescription(
                    "Default magnification when auto-detection fails (e.g. fixed scopes without zoom data).",
                    new AcceptableValueRange<float>(1f, 16f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoFovFromScope = Config.Bind("General", "AutoFovFromScope", true,
                new ConfigDescription(
                    "Auto-detect magnification from the scope's zoom data (ScopeZoomHandler). " +
                "Works for variable-zoom scopes. Falls back to DefaultZoom for fixed scopes.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopedFov = Config.Bind("General", "ScopedFov", 15f,
                new ConfigDescription(
                    "FOV (degrees) for FOV zoom fallback mode. Lower = more zoom. " +
                    "Used for FOV zoom.",
                    new AcceptableValueRange<float>(5f, 75f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            FovAnimationDuration = Config.Bind("General", "FovAnimationDuration", 0.25f,
                new ConfigDescription(
                    "Duration of the FOV transitions during magnification changes.",
                    new AcceptableValueRange<float>(0f, 10f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

            ManualLodBias = Config.Bind("Optimization", "ManualLodBias", 4.0f,
                new ConfigDescription(
                    "Manual LOD bias while scoped.\n" +
                    "0 = auto (baseLodBias * magnification).\n" +
                    ">0 = force this exact value (e.g. 4.0).",
                    new AcceptableValueRange<float>(0f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ManualMaximumLodLevel = Config.Bind("Optimization", "ManualMaximumLodLevel", -1,
                new ConfigDescription(
                    "Manual QualitySettings.maximumLODLevel while scoped.\n" +
                    "-1 = auto (force 0 / highest detail).\n" +
                    ">=0 = force this exact max LOD level.",
                    new AcceptableValueRange<int>(-1, 8),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ManualCullingMultiplier = Config.Bind("Optimization", "ManualCullingMultiplier", 0.8f,
                new ConfigDescription(
                    "Manual multiplier for Camera.layerCullDistances while scoped.\n" +
                    "0 = auto (use magnification).\n" +
                    ">0 = force this multiplier (e.g. 2.0 doubles cull distances).",
                    new AcceptableValueRange<float>(0f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            MapManualLodBias = new Dictionary<string, ConfigEntry<float>>(StringComparer.OrdinalIgnoreCase);

            BindPerMapLodBias("Woods", "Woods", "Woods");
            BindPerMapLodBias("Factory", "Factory", "factory4_day", "factory4_night");
            BindPerMapLodBias("Customs", "Customs", "bigmap");
            BindPerMapLodBias("Shoreline", "Shoreline", "Shoreline");
            BindPerMapLodBias("Interchange", "Interchange", "Interchange");
            BindPerMapLodBias("Reserve", "Reserve", "RezervBase");
            BindPerMapLodBias("TheLab", "The Lab", "laboratory");
            BindPerMapLodBias("Lighthouse", "Lighthouse", "Lighthouse");
            BindPerMapLodBias("StreetsOfTarkov", "Streets of Tarkov", "TarkovStreets");
            BindPerMapLodBias("GroundZero", "Ground Zero", "Sandbox", "Sandbox_high");
            ZoomToggleKey = Config.Bind("General", "ZoomToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for zoom (None = always on when EnableZoom is true).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- 4. Zeroing ---
            EnableZeroing = Config.Bind("4. Zeroing", "EnableZeroing", true,
                new ConfigDescription(
                    "Enable optic zeroing (calibration distance adjustment) via keyboard.\n" +
                "Uses the proper EFT pathway (works with Fika).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ZeroingUpKey = Config.Bind("4. Zeroing", "ZeroingUpKey", KeyCode.PageUp,
                new ConfigDescription(
                    "Key to increase zeroing distance.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ZeroingDownKey = Config.Bind("4. Zeroing", "ZeroingDownKey", KeyCode.PageDown,
                new ConfigDescription(
                    "Key to decrease zeroing distance.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Mesh Surgery (ON by default, Cylinder mode) ---
            EnableMeshSurgery = Config.Bind("Global Mesh Surgery settings", "EnableMeshSurgery", true,
                new ConfigDescription(
                    "Enable runtime mesh cutting to bore a hole through the scope housing.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            MeshSurgeryToggleKey = Config.Bind("Global Mesh Surgery settings", "MeshSurgeryToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for mesh surgery.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RestoreOnUnscope = Config.Bind("Global Mesh Surgery settings", "RestoreOnUnscope", true,
                new ConfigDescription(
                    "Restore original meshes when leaving scope.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            PlaneOffsetMeters = Config.Bind("Global Mesh Surgery settings", "PlaneOffsetMeters", 0.001f,
                new ConfigDescription(
                    "Offset applied along plane normal (meters).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            PlaneNormalAxis = Config.Bind("Global Mesh Surgery settings", "PlaneNormalAxis", "-Y",
                new ConfigDescription(
                    "Which local axis to use as the cut plane normal.\n" +
                    "Auto = use backLens.forward (game default).\n" +
                    "X/Y/Z = force that local axis as the plane normal.\n" +
                    "-X/-Y/-Z = force the negative of that axis.\n" +
                    "If the cut is horizontal when it should be vertical, try Z or Y.",
                    new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z"),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutRadius = Config.Bind("Global Mesh Surgery settings", "CutRadius", 0f,
                new ConfigDescription(
                    "Max distance (meters) from scope center to cut. 0 = unlimited (cut all geometry).\n" +
                    "Set to e.g. 0.05 to only cut geometry near the lens opening.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ShowCutPlane = Config.Bind("Global Mesh Surgery settings", "ShowCutPlane", false,
                new ConfigDescription(
                    "Show green/red semi-transparent circles at the near/far cut plane positions.\n" +
                "Use this to visualize the cut endpoints.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ShowCutVolume = Config.Bind("Global Mesh Surgery settings", "ShowCutVolume", false,
                new ConfigDescription(
                    "Show a semi-transparent 3D tube representing the full cut volume.\n" +
                "Visualizes the near→mid→far radius profile so you can see exactly what gets removed.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutVolumeOpacity = Config.Bind("Global Mesh Surgery settings", "CutVolumeOpacity", 0.49f,
                new ConfigDescription(
                    "Opacity of the 3D cut volume visualizer (0 = invisible, 1 = opaque).",
                    new AcceptableValueRange<float>(0.05f, 0.8f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutMode = Config.Bind("Global Mesh Surgery settings", "CutMode", "Cylinder",
                new ConfigDescription(
                    "Plane = flat infinite cut. Cylinder = cylindrical bore cut centered on the lens axis.\n" +
                    "Cylinder removes geometry inside a cylinder of CylinderRadius around the lens center.",
                    new AcceptableValueList<string>("Plane", "Cylinder"),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CylinderRadius = Config.Bind("Global Mesh Surgery settings", "CylinderRadius", 0.011f,
                new ConfigDescription(
                    "Near radius (meters) of the cylindrical/cone cut (camera side).\n" +
                    "Typical scope lens radius is ~0.01-0.02m.",
                    new AcceptableValueRange<float>(0.001f, 0.1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            MidCylinderRadius = Config.Bind("Global Mesh Surgery settings", "MidCylinderRadius", 0.013f,
                new ConfigDescription(
                    "Intermediate radius (meters) at MidCylinderPosition along the bore.\n" +
                    "0 = disabled (linear near→far interpolation).\n" +
                    ">0 = two-segment interpolation: near→mid, then mid→far.\n" +
                    "Set smaller than near/far to create a waist (hourglass). Set larger for a bulge.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            MidCylinderPosition = Config.Bind("Global Mesh Surgery settings", "MidCylinderPosition", 0.28f,
                new ConfigDescription(
                    "Position of the mid-radius control point along the cut length (0=near, 1=far).\n" +
                    "0.5 = midpoint. 0.3 = closer to camera. 0.7 = closer to objective.",
                    new AcceptableValueRange<float>(0.01f, 0.99f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            FarCylinderRadius = Config.Bind("Global Mesh Surgery settings", "FarCylinderRadius", 0.12f,
                new ConfigDescription(
                    "Far radius (meters) of the cone cut (objective side).\n" +
                    "0 = same as CylinderRadius (pure cylinder). >0 creates a cone/frustum shape.\n" +
                    "Set larger than CylinderRadius to widen the bore toward the objective lens.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane1OffsetMeters = Config.Bind("Global Mesh Surgery settings", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Offset (meters) for plane 1 from linza/backLens origin along bore axis.\n" +
                    "Plane 1 radius is always CylinderRadius.",
                    new AcceptableValueRange<float>(-0.02f, 0.02f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane2Position = Config.Bind("Global Mesh Surgery settings", "Plane2Position", 0.1138498f,
                new ConfigDescription(
                    "Plane 2 profile position (0..1) anchored from the near side.\n" +
                    "Changing CutLength keeps this plane at the same world-space depth from near.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane2Radius = Config.Bind("Global Mesh Surgery settings", "Plane2Radius", 0.0186338f,
                new ConfigDescription(
                    "Radius (meters) at plane 2.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane3Position = Config.Bind("Global Mesh Surgery settings", "Plane3Position", 0.55f,
                new ConfigDescription(
                    "Normalized depth of plane 3 from near (0) to far (1).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane3Radius = Config.Bind("Global Mesh Surgery settings", "Plane3Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 3.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane4Position = Config.Bind("Global Mesh Surgery settings", "Plane4Position", 1f,
                new ConfigDescription(
                    "Normalized depth of plane 4 from near (0) to far (1). Usually 1.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane4Radius = Config.Bind("Global Mesh Surgery settings", "Plane4Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 4 (away from player).",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutStartOffset = Config.Bind("Global Mesh Surgery settings", "CutStartOffset", 0.04084507f,
                new ConfigDescription(
                    "How far behind the backLens (toward the camera) the near cut plane starts.\n" +
                    "The near plane is fixed at: backLens - (offset × boreAxis).\n" +
                    "0 = starts exactly at the backLens. 0.05 = 5cm behind it (catches interior tube geometry).\n" +
                    "Changing CutLength does NOT move this plane.",
                    new AcceptableValueRange<float>(0f, 0.3f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutLength = Config.Bind("Global Mesh Surgery settings", "CutLength", 0.755493f,
                new ConfigDescription(
                    "How far forward from the near plane the cut extends (toward the objective).\n" +
                    "The far plane is at: nearPlane + (length × boreAxis).\n" +
                    "Only the far plane moves when you change this value.",
                    new AcceptableValueRange<float>(0.01f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            NearPreserveDepth = Config.Bind("Global Mesh Surgery settings", "NearPreserveDepth", 0.02549295f,
                new ConfigDescription(
                    "Depth (meters) from the near cut plane where NO geometry is cut.\n" +
                    "Preserves the eyepiece housing closest to the camera so you\n" +
                    "keep a thin ring framing the reticle while ADSing.\n" +
                    "0.01 = 1cm ring (default). 0.02-0.04 = thicker ring.\n" +
                    "0 = disabled (cut starts at the near plane — removes everything).",
                    new AcceptableValueRange<float>(0f, 0.15f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ShowReticle = Config.Bind("Global Mesh Surgery settings", "ShowReticle", true,
                new ConfigDescription(
                    "Render the scope reticle texture as a glowing overlay where the lens was.\n" +
                "Uses alpha blending so the reticle's own alpha channel controls transparency.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ReticleBaseSize = Config.Bind("Global Mesh Surgery settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Physical diameter (meters) of the reticle quad at 1x magnification.\n" +
                    "The quad is scaled by 1/magnification so screen-pixel coverage stays constant\n" +
                    "across all zoom levels.  Typical scope lens diameter is 0.02-0.04 m.\n" +
                    "Set to 0 to fall back to the legacy CylinderRadius x2 value.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ExpandSearchToWeaponRoot = Config.Bind("Global Mesh Surgery settings", "ExpandSearchToWeaponRoot", true,
                new ConfigDescription(
                    "Expand the mesh surgery search root all the way up to the Weapon_root node.\n" +
                "When enabled, meshes on the weapon body under Weapon_root are also candidates\n" +
                "for cutting — not just those in the scope sub-hierarchy.\n" +
                "Use this when scope geometry blends into the weapon receiver and you need to cut\n" +
                "the underlying weapon meshes as well.\n" +
                "Example path: Weapon_root/Weapon_root_anim/weapon/mod_scope/...",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            DebugShowHousingMask = Config.Bind("Global Mesh Surgery settings", "DebugShowHousingMask", false,
                new ConfigDescription(
                    "Render a red/yellow overlay wherever the scope housing stencil mask is\n" +
                "suppressing the reticle.  Use this to diagnose which meshes are incorrectly\n" +
                "masking the aperture.  Combine with the BepInEx log to see the exact renderer\n" +
                "names printed by CollectHousingRenderers.  Disable in normal play.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            StencilIncludeWeaponMeshes = Config.Bind("Global Mesh Surgery settings", "StencilIncludeWeaponMeshes", true,
                new ConfigDescription(
                    "Include weapon body renderers (found under the 'weapon' transform) in the\n" +
                "stencil mask alongside the scope housing.  Prevents the reticle from\n" +
                "bleeding through the weapon mesh at screen centre.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Custom Mesh Surgery settings ---
            SaveCustomMeshSurgerySettingsKey = Config.Bind("Per scope settings", "SaveCustomMeshSurgerySettingsKey", KeyCode.None,
                new ConfigDescription(
                    "When pressed while scoped, save all values from this category for the active scope key into custom_mesh_surgery_settings.json.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            DeleteCustomMeshSurgerySettingsKey = Config.Bind("Per scope settings", "DeleteCustomMeshSurgerySettingsKey", KeyCode.None,
                new ConfigDescription(
                    "When pressed while scoped, delete custom mesh surgery settings for the active scope key from custom_mesh_surgery_settings.json.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlaneOffsetMeters = Config.Bind("Per scope settings", "PlaneOffsetMeters", 0.001f,
                new ConfigDescription(
                    "Custom per-scope plane offset applied along plane normal (meters).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlaneNormalAxis = Config.Bind("Per scope settings", "PlaneNormalAxis", "-Y",
                new ConfigDescription(
                    "Custom per-scope local axis for the cut plane normal.",
                    new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z"),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutRadius = Config.Bind("Per scope settings", "CutRadius", 0f,
                new ConfigDescription(
                    "Custom per-scope max cut distance in meters (0 = unlimited).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomShowCutPlane = Config.Bind("Per scope settings", "ShowCutPlane", false,
                new ConfigDescription(
                    "Custom per-scope cut plane visualizer toggle.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomShowCutVolume = Config.Bind("Per scope settings", "ShowCutVolume", false,
                new ConfigDescription(
                    "Custom per-scope cut volume visualizer toggle.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutVolumeOpacity = Config.Bind("Per scope settings", "CutVolumeOpacity", 0.49f,
                new ConfigDescription(
                    "Custom per-scope cut volume opacity.",
                    new AcceptableValueRange<float>(0.05f, 0.8f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutMode = Config.Bind("Per scope settings", "CutMode", "Cylinder",
                new ConfigDescription(
                    "Custom per-scope mesh cut mode.",
                    new AcceptableValueList<string>("Plane", "Cylinder"),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCylinderRadius = Config.Bind("Per scope settings", "CylinderRadius", 0.011f,
                new ConfigDescription(
                    "Custom per-scope near radius in meters.",
                    new AcceptableValueRange<float>(0.001f, 0.1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomMidCylinderRadius = Config.Bind("Per scope settings", "MidCylinderRadius", 0.013f,
                new ConfigDescription(
                    "Custom per-scope mid profile radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomMidCylinderPosition = Config.Bind("Per scope settings", "MidCylinderPosition", 0.28f,
                new ConfigDescription(
                    "Custom per-scope mid profile position (0..1).",
                    new AcceptableValueRange<float>(0.01f, 0.99f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomFarCylinderRadius = Config.Bind("Per scope settings", "FarCylinderRadius", 0.12f,
                new ConfigDescription(
                    "Custom per-scope far radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane1OffsetMeters = Config.Bind("Per scope settings", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Custom per-scope plane 1 offset in meters.",
                    new AcceptableValueRange<float>(-0.02f, 0.02f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane2Position = Config.Bind("Per scope settings", "Plane2Position", 0.1138498f,
                new ConfigDescription(
                    "Custom per-scope plane 2 position (0..1), anchored from near when CutLength changes.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane2Radius = Config.Bind("Per scope settings", "Plane2Radius", 0.0186338f,
                new ConfigDescription(
                    "Custom per-scope plane 2 radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane3Position = Config.Bind("Per scope settings", "Plane3Position", 0.55f,
                new ConfigDescription(
                    "Custom per-scope plane 3 depth (0..1).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane3Radius = Config.Bind("Per scope settings", "Plane3Radius", 0.2f,
                new ConfigDescription(
                    "Custom per-scope plane 3 radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane4Position = Config.Bind("Per scope settings", "Plane4Position", 1f,
                new ConfigDescription(
                    "Custom per-scope plane 4 depth (0..1).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane4Radius = Config.Bind("Per scope settings", "Plane4Radius", 0.2f,
                new ConfigDescription(
                    "Custom per-scope plane 4 radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutStartOffset = Config.Bind("Per scope settings", "CutStartOffset", 0.04084507f,
                new ConfigDescription(
                    "Custom per-scope cut start offset in meters.",
                    new AcceptableValueRange<float>(-0.2f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutLength = Config.Bind("Per scope settings", "CutLength", 0.755493f,
                new ConfigDescription(
                    "Custom per-scope cut length in meters.",
                    new AcceptableValueRange<float>(0.01f, 4f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomNearPreserveDepth = Config.Bind("Per scope settings", "NearPreserveDepth", 0.02549295f,
                new ConfigDescription(
                    "Custom per-scope near preserve depth in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomShowReticle = Config.Bind("Per scope settings", "ShowReticle", true,
                new ConfigDescription(
                    "Custom per-scope reticle visibility.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomReticleBaseSize = Config.Bind("Per scope settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Custom per-scope reticle base diameter in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            CustomRestoreOnUnscope = Config.Bind("Per scope settings", "RestoreOnUnscope", true,
                new ConfigDescription(
                    "Custom per-scope restore behavior when leaving scope.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomExpandSearchToWeaponRoot = Config.Bind("Per scope settings", "ExpandSearchToWeaponRoot", true,
                new ConfigDescription(
                    "Custom per-scope search root expansion to Weapon_root.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Scope Effects ---
            VignetteEnabled = Config.Bind("Scope Effects", "Vignette", false,
                new ConfigDescription(
                    "Render a circular vignette ring around the scope aperture.\n" +
                "A world-space quad at the lens position fading from transparent centre to black edge.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteOpacity = Config.Bind("Scope Effects", "Vignette Opacity", 0.39f,
                new ConfigDescription(
                    "Maximum opacity of the lens vignette ring (0=invisible, 1=full black).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteSizeMult = Config.Bind("Scope Effects", "Vignette Size Multiplier", 0.35f,
                new ConfigDescription(
                    "Vignette quad diameter as a multiplier of ReticleBaseSize.\n" +
                    "1.0 = same size as reticle.  1.5 gives a visible border ring.\n" +
                    "Higher values (5-15) may be needed for high-magnification scopes.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteSoftness = Config.Bind("Scope Effects", "Vignette Softness", 0.51f,
                new ConfigDescription(
                    "Fraction of the vignette radius used for the gradient falloff (0=hard edge, 1=full gradient).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

            ScopeShadowEnabled = Config.Bind("Scope Effects", "ScopeShadow", false,
                new ConfigDescription(
                    "Overlay a fullscreen scope-tube shadow: black everywhere except a transparent\n" +
                "circular window in the centre.  Simulates looking down a scope tube.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowOpacity = Config.Bind("Scope Effects", "ScopeShadow Opacity", 0.75f,
                new ConfigDescription(
                    "Maximum opacity of the scope shadow overlay.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowRadius = Config.Bind("Scope Effects", "ScopeShadow Radius", 0.07859156f,
                new ConfigDescription(
                    "Radius of the transparent centre window as a fraction of the half-screen (0.0-0.5).\n" +
                    "0.18 = window fills roughly 36% of screen width.  Increase for wider aperture.",
                    new AcceptableValueRange<float>(0.02f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowSoftness = Config.Bind("Scope Effects", "ScopeShadow Softness", 0.08535211f,
                new ConfigDescription(
                    "Width of the gradient edge between the clear window and the black shadow (fraction of screen).\n" +
                    "0 = hard edge.  0.05-0.1 is a natural-looking falloff.",
                    new AcceptableValueRange<float>(0f, 0.3f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

            // --- Diagnostics ---
            DiagnosticsKey = Config.Bind("Diagnostics", "DiagnosticsKey", KeyCode.None,
                new ConfigDescription(
                    "Press to log full diagnostics for the currently active scope: name, hierarchy,\n" +
                "magnification and cut-plane config.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

            // --- Debug ---
            VerboseLogging = Config.Bind("Diagnostics", "VerboseLogging", false,
                new ConfigDescription(
                    "Enable detailed logging. Turn on to diagnose lens/zoom issues.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            DebugLogCutCandidates = Config.Bind("Diagnostics", "DebugLogCutCandidates", false,
                new ConfigDescription(
                    "When enabled, logs every mesh candidate found by mesh surgery (path, mesh name, vertices, active state), " +
                "plus per-candidate radius checks. Useful to diagnose attachments that are not being cut.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            DebugReticleAfterEverything = Config.Bind("General", "Draw reticle after everything", false,
                new ConfigDescription(
                    "When enabled, reticle is always clear but doesn't get tinted by NVGs",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

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

        private void BindPerMapLodBias(string configKeySuffix, string mapDisplayName, params string[] locationIds)
        {
            if (locationIds == null || locationIds.Length == 0 || string.IsNullOrWhiteSpace(configKeySuffix))
                return;

            var entry = Config.Bind("General Per-Map", $"ManualLodBias_{configKeySuffix}", ManualLodBias.Value,
                new ConfigDescription(
                    $"Manual LOD bias override while scoped on map '{mapDisplayName}'.\n" +
                    "0 = auto (baseLodBias * magnification).\n" +
                    ">0 = force this exact value (e.g. 4.0).",
                    new AcceptableValueRange<float>(0f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            for (int i = 0; i < locationIds.Length; i++)
            {
                string locationId = locationIds[i];
                if (string.IsNullOrWhiteSpace(locationId))
                    continue;
                MapManualLodBias[locationId] = entry;
            }
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
    }
}
