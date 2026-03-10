using System;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using System.IO;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    [BepInPlugin("com.fiodor.pipdisabler", "PiP-Disabler", "0.1.0")]
    public sealed class ScopeHousingMeshSurgeryPlugin : BaseUnityPlugin
    {
        internal static ScopeHousingMeshSurgeryPlugin Instance;

        // --- Logging helpers ---
        internal static void LogInfo(string msg) { if (Instance != null) Instance.Logger.LogInfo(msg); }
        internal static void LogWarn(string msg) { if (Instance != null) Instance.Logger.LogWarning(msg); }
        internal static void LogError(string msg) { if (Instance != null) Instance.Logger.LogError(msg); }
        internal static void LogVerbose(string msg)
        {
            if (Instance != null && VerboseLogging != null && VerboseLogging.Value)
                Instance.Logger.LogInfo("[V] " + msg);
        }

        internal static string GetMeshCutCacheDirectory()
        {
            string pluginDir;
            try
            {
                pluginDir = Path.GetDirectoryName(Instance != null ? Instance.Info.Location : null);
            }
            catch
            {
                pluginDir = null;
            }

            if (string.IsNullOrEmpty(pluginDir))
                pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "plugins");

            string cacheDir = Path.Combine(pluginDir, "mesh_cut_cache");
            if (!Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);

            return cacheDir;
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

        // --- 0. Global ---
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<KeyCode> ModToggleKey;

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
        internal static ConfigEntry<bool> ClearMeshCacheOnRaidEnd;
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
        internal static ConfigEntry<bool> ReticleOverlayCamera;
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
        internal static ConfigEntry<bool> CustomReticleOverlayCamera;
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

        // --- 4. Zeroing ---
        internal static ConfigEntry<bool> EnableZeroing;
        internal static ConfigEntry<KeyCode> ZeroingUpKey;
        internal static ConfigEntry<KeyCode> ZeroingDownKey;

        // --- Debug ---
        internal static ConfigEntry<bool> VerboseLogging;

        private bool _wasInRaid;

        private void Awake()
        {
            Instance = this;

            // --- 0. Global ---
            ModEnabled = Config.Bind("0. Global", "ModEnabled", true,
                "Master ON/OFF switch for the entire mod. When OFF, all effects are " +
                "cleaned up and the game behaves as if the mod is not installed.");
            ModToggleKey = Config.Bind("0. Global", "ModToggleKey", KeyCode.Backspace,
                "Toggle key for master mod enable/disable.");

            // --- General ---
            DisablePiP = Config.Bind("1. General", "DisablePiP", true,
                "Disable Picture-in-Picture optic rendering (No-PiP mode). " +
                "Core feature — gives identical perf between hip-fire and ADS.");
            AutoDisableForVariableScopes = Config.Bind("1. General", "AutoDisableForVariableScopes", true,
                "Automatically disable all mod effects while scoped with variable magnification optics (IsAdjustableOptic=true).\n" +
                "Also bypasses thermal/night-vision scopes detected via ScopeData.ThermalVisionData or NightVisionData.");
            AutoBypassNameContains = Config.Bind("1. General", "AutoBypassNameContains", "npz",
                "Comma-separated list of substrings. Any scope whose object name or scope key contains one of these " +
                "(case-insensitive) is automatically bypassed, the same way variable/NV scopes are.\n" +
                "Default 'npz' covers NPZ passive night-vision scopes (PAG-17, etc.) that lack a NightVisionData component.");
            ScopeWhitelistNames = Config.Bind("1. General", "ScopeWhitelistNames", "",
                "Comma/semicolon/newline separated list of allowed scope keys.\n" +
                "Primary key is derived from the object under mod_scope that does not contain mount (case-insensitive).\n" +
                "Fallbacks: template _name, template _id, then optic object name. Empty list = whitelist ignored.");
            ScopeWhitelistToggleEntryKey = Config.Bind("1. General", "ScopeWhitelistToggleEntryKey", KeyCode.None,
                "When pressed while scoped, add/remove the current scope key in ScopeWhitelistNames (derived from mod_scope non-mount object).");
            DisablePiPToggleKey = Config.Bind("1. General", "DisablePiPToggleKey", KeyCode.F10,
                "Toggle key for PiP disable.");

            MakeLensesTransparent = Config.Bind("1. General", "MakeLensesTransparent", true,
                "Hide lens surfaces (linza/backLens) while scoped so you see through the tube.");
            LensesTransparentToggleKey = Config.Bind("1. General", "LensesTransparentToggleKey", KeyCode.F11,
                "Toggle key for lens transparency.");
            BlackLensWhenUnscoped = Config.Bind("1. General", "BlackLensWhenUnscoped", true,
                "When unscoping, apply a solid black opaque material to the lens instead of restoring " +
                "the original PiP/sight material. Eliminates the reticle flash during the unscope " +
                "transition and gives the scope a realistic dark-glass appearance when not in use.");

            // --- Weapon Scaling ---
            EnableWeaponScaling = Config.Bind("2. Zoom", "EnableWeaponScaling", true,
                "Compensate weapon/arms model scale across magnification levels.\n" +
                "Without this, zooming in (lower FOV) makes the weapon appear larger on screen.\n" +
                "With this enabled, the weapon shrinks proportionally as you zoom in so it\n" +
                "always occupies the same screen space at every magnification level.");
            BaselineWeaponScale = Config.Bind("2. Zoom", "BaselineWeaponScale", 0.9624413f,
                new ConfigDescription(
                    "Base weapon scale applied at all FOV values before compensation.\n" +
                    "1.00 = default EFT visual scale.",
                    new AcceptableValueRange<float>(0.00f, 2.00f)));
            WeaponScaleStrength = Config.Bind("2. Zoom", "WeaponScaleStrength", 0.2723005f,
                new ConfigDescription(
                    "Blends between no compensation and full inverse-FOV compensation.\n" +
                    "0.00 = no compensation, 1.00 = full compensation, values outside [0,1] over/under-compensate.",
                    new AcceptableValueRange<float>(-2.00f, 2.00f)));

            // --- Zoom ---
            EnableZoom = Config.Bind("2. Zoom", "EnableZoom", true,
                "Enable scope magnification via FOV zoom.");
            DefaultZoom = Config.Bind("2. Zoom", "DefaultZoom", 4f,
                new ConfigDescription(
                    "Default magnification when auto-detection fails (e.g. fixed scopes without zoom data).",
                    new AcceptableValueRange<float>(1f, 16f)));
            AutoFovFromScope = Config.Bind("2. Zoom", "AutoFovFromScope", true,
                "Auto-detect magnification from the scope's zoom data (ScopeZoomHandler). " +
                "Works for variable-zoom scopes. Falls back to DefaultZoom for fixed scopes.");
            ScopedFov = Config.Bind("2. Zoom", "ScopedFov", 15f,
                new ConfigDescription(
                    "FOV (degrees) for FOV zoom fallback mode. Lower = more zoom. " +
                    "Used for FOV zoom.",
                    new AcceptableValueRange<float>(5f, 75f)));
            FovAnimationDuration = Config.Bind("2. Zoom", "FovAnimationDuration", 1f,
                new ConfigDescription(
                    "Duration (seconds) of the FOV zoom-in animation when entering ADS.\n" +
                    "0 = instant snap. 0.25 = smooth quarter-second transition.\n" +
                    "Scope exit always restores FOV instantly to avoid sluggish feel.",
                    new AcceptableValueRange<float>(0f, 2f)));

            ManualLodBias = Config.Bind("2. Zoom", "ManualLodBias", 4.0f,
                new ConfigDescription(
                    "Manual LOD bias while scoped.\n" +
                    "0 = auto (baseLodBias * magnification).\n" +
                    ">0 = force this exact value (e.g. 4.0).",
                    new AcceptableValueRange<float>(0f, 20f)));
            ManualMaximumLodLevel = Config.Bind("2. Zoom", "ManualMaximumLodLevel", -1,
                new ConfigDescription(
                    "Manual QualitySettings.maximumLODLevel while scoped.\n" +
                    "-1 = auto (force 0 / highest detail).\n" +
                    ">=0 = force this exact max LOD level.",
                    new AcceptableValueRange<int>(-1, 8)));
            ManualCullingMultiplier = Config.Bind("2. Zoom", "ManualCullingMultiplier", 0.8f,
                new ConfigDescription(
                    "Manual multiplier for Camera.layerCullDistances while scoped.\n" +
                    "0 = auto (use magnification).\n" +
                    ">0 = force this multiplier (e.g. 2.0 doubles cull distances).",
                    new AcceptableValueRange<float>(0f, 20f)));
            ZoomToggleKey = Config.Bind("2. Zoom", "ZoomToggleKey", KeyCode.None,
                "Toggle key for zoom (None = always on when EnableZoom is true).");

            // --- 4. Zeroing ---
            EnableZeroing = Config.Bind("4. Zeroing", "EnableZeroing", true,
                "Enable optic zeroing (calibration distance adjustment) via keyboard.\n" +
                "Uses the proper EFT pathway (works with Fika).");
            ZeroingUpKey = Config.Bind("4. Zeroing", "ZeroingUpKey", KeyCode.PageUp,
                "Key to increase zeroing distance.");
            ZeroingDownKey = Config.Bind("4. Zeroing", "ZeroingDownKey", KeyCode.PageDown,
                "Key to decrease zeroing distance.");

            // --- Mesh Surgery (ON by default, Cylinder mode) ---
            EnableMeshSurgery = Config.Bind("3. Global Mesh Surgery settings", "EnableMeshSurgery", true,
                "Enable runtime mesh cutting to bore a hole through the scope housing.");
            MeshSurgeryToggleKey = Config.Bind("3. Global Mesh Surgery settings", "MeshSurgeryToggleKey", KeyCode.F9,
                "Toggle key for mesh surgery.");
            RestoreOnUnscope = Config.Bind("3. Global Mesh Surgery settings", "RestoreOnUnscope", true,
                "Restore original meshes when leaving scope.");
            ClearMeshCacheOnRaidEnd = Config.Bind("3. Global Mesh Surgery settings", "ClearMeshCacheOnRaidEnd", true,
                "Clear persisted mesh-cut cache files when transitioning from raid to out-of-raid.");
            PlaneOffsetMeters = Config.Bind("3. Global Mesh Surgery settings", "PlaneOffsetMeters", 0.001f,
                "Offset applied along plane normal (meters).");
            PlaneNormalAxis = Config.Bind("3. Global Mesh Surgery settings", "PlaneNormalAxis", "-Y",
                new ConfigDescription(
                    "Which local axis to use as the cut plane normal.\n" +
                    "Auto = use backLens.forward (game default).\n" +
                    "X/Y/Z = force that local axis as the plane normal.\n" +
                    "-X/-Y/-Z = force the negative of that axis.\n" +
                    "If the cut is horizontal when it should be vertical, try Z or Y.",
                    new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z")));
            CutRadius = Config.Bind("3. Global Mesh Surgery settings", "CutRadius", 0f,
                new ConfigDescription(
                    "Max distance (meters) from scope center to cut. 0 = unlimited (cut all geometry).\n" +
                    "Set to e.g. 0.05 to only cut geometry near the lens opening.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ShowCutPlane = Config.Bind("3. Global Mesh Surgery settings", "ShowCutPlane", false,
                "Show green/red semi-transparent circles at the near/far cut plane positions.\n" +
                "Use this to visualize the cut endpoints.");
            ShowCutVolume = Config.Bind("3. Global Mesh Surgery settings", "ShowCutVolume", false,
                "Show a semi-transparent 3D tube representing the full cut volume.\n" +
                "Visualizes the near→mid→far radius profile so you can see exactly what gets removed.");
            CutVolumeOpacity = Config.Bind("3. Global Mesh Surgery settings", "CutVolumeOpacity", 0.49f,
                new ConfigDescription(
                    "Opacity of the 3D cut volume visualizer (0 = invisible, 1 = opaque).",
                    new AcceptableValueRange<float>(0.05f, 0.8f)));
            CutMode = Config.Bind("3. Global Mesh Surgery settings", "CutMode", "Cylinder",
                new ConfigDescription(
                    "Plane = flat infinite cut. Cylinder = cylindrical bore cut centered on the lens axis.\n" +
                    "Cylinder removes geometry inside a cylinder of CylinderRadius around the lens center.",
                    new AcceptableValueList<string>("Plane", "Cylinder")));
            CylinderRadius = Config.Bind("3. Global Mesh Surgery settings", "CylinderRadius", 0.011f,
                new ConfigDescription(
                    "Near radius (meters) of the cylindrical/cone cut (camera side).\n" +
                    "Typical scope lens radius is ~0.01-0.02m.",
                    new AcceptableValueRange<float>(0.001f, 0.1f)));
            MidCylinderRadius = Config.Bind("3. Global Mesh Surgery settings", "MidCylinderRadius", 0.013f,
                new ConfigDescription(
                    "Intermediate radius (meters) at MidCylinderPosition along the bore.\n" +
                    "0 = disabled (linear near→far interpolation).\n" +
                    ">0 = two-segment interpolation: near→mid, then mid→far.\n" +
                    "Set smaller than near/far to create a waist (hourglass). Set larger for a bulge.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            MidCylinderPosition = Config.Bind("3. Global Mesh Surgery settings", "MidCylinderPosition", 0.28f,
                new ConfigDescription(
                    "Position of the mid-radius control point along the cut length (0=near, 1=far).\n" +
                    "0.5 = midpoint. 0.3 = closer to camera. 0.7 = closer to objective.",
                    new AcceptableValueRange<float>(0.01f, 0.99f)));
            FarCylinderRadius = Config.Bind("3. Global Mesh Surgery settings", "FarCylinderRadius", 0.12f,
                new ConfigDescription(
                    "Far radius (meters) of the cone cut (objective side).\n" +
                    "0 = same as CylinderRadius (pure cylinder). >0 creates a cone/frustum shape.\n" +
                    "Set larger than CylinderRadius to widen the bore toward the objective lens.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            Plane1OffsetMeters = Config.Bind("3. Global Mesh Surgery settings", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Offset (meters) for plane 1 from linza/backLens origin along bore axis.\n" +
                    "Plane 1 radius is always CylinderRadius.",
                    new AcceptableValueRange<float>(-0.02f, 0.02f)));
            Plane2Position = Config.Bind("3. Global Mesh Surgery settings", "Plane2Position", 0.1138498f,
                new ConfigDescription(
                    "Plane 2 profile position (0..1) anchored from the near side.\n" +
                    "Changing CutLength keeps this plane at the same world-space depth from near.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Plane2Radius = Config.Bind("3. Global Mesh Surgery settings", "Plane2Radius", 0.0186338f,
                new ConfigDescription(
                    "Radius (meters) at plane 2.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            Plane3Position = Config.Bind("3. Global Mesh Surgery settings", "Plane3Position", 0.55f,
                new ConfigDescription(
                    "Normalized depth of plane 3 from near (0) to far (1).",
                    new AcceptableValueRange<float>(0f, 1f)));
            Plane3Radius = Config.Bind("3. Global Mesh Surgery settings", "Plane3Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 3.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            Plane4Position = Config.Bind("3. Global Mesh Surgery settings", "Plane4Position", 1f,
                new ConfigDescription(
                    "Normalized depth of plane 4 from near (0) to far (1). Usually 1.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Plane4Radius = Config.Bind("3. Global Mesh Surgery settings", "Plane4Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 4 (away from player).",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            CutStartOffset = Config.Bind("3. Global Mesh Surgery settings", "CutStartOffset", 0.04084507f,
                new ConfigDescription(
                    "How far behind the backLens (toward the camera) the near cut plane starts.\n" +
                    "The near plane is fixed at: backLens - (offset × boreAxis).\n" +
                    "0 = starts exactly at the backLens. 0.05 = 5cm behind it (catches interior tube geometry).\n" +
                    "Changing CutLength does NOT move this plane.",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            CutLength = Config.Bind("3. Global Mesh Surgery settings", "CutLength", 0.755493f,
                new ConfigDescription(
                    "How far forward from the near plane the cut extends (toward the objective).\n" +
                    "The far plane is at: nearPlane + (length × boreAxis).\n" +
                    "Only the far plane moves when you change this value.",
                    new AcceptableValueRange<float>(0.01f, 1f)));
            NearPreserveDepth = Config.Bind("3. Global Mesh Surgery settings", "NearPreserveDepth", 0.02549295f,
                new ConfigDescription(
                    "Depth (meters) from the near cut plane where NO geometry is cut.\n" +
                    "Preserves the eyepiece housing closest to the camera so you\n" +
                    "keep a thin ring framing the reticle while ADSing.\n" +
                    "0.01 = 1cm ring (default). 0.02-0.04 = thicker ring.\n" +
                    "0 = disabled (cut starts at the near plane — removes everything).",
                    new AcceptableValueRange<float>(0f, 0.15f)));
            ShowReticle = Config.Bind("3. Global Mesh Surgery settings", "ShowReticle", true,
                "Render the scope reticle texture as a glowing overlay where the lens was.\n" +
                "Uses alpha blending so the reticle's own alpha channel controls transparency.");
            ReticleBaseSize = Config.Bind("3. Global Mesh Surgery settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Physical diameter (meters) of the reticle quad at 1x magnification.\n" +
                    "The quad is scaled by 1/magnification so screen-pixel coverage stays constant\n" +
                    "across all zoom levels.  Typical scope lens diameter is 0.02-0.04 m.\n" +
                    "Set to 0 to fall back to the legacy CylinderRadius x2 value.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            ReticleOverlayCamera = Config.Bind("3. Global Mesh Surgery settings", "ReticleOverlayCamera", true,
                "[DEPRECATED — reticle now uses a CommandBuffer with nonJitteredProjectionMatrix.\n" +
                "The overlay camera has been removed. This setting has no effect.]");
            ExpandSearchToWeaponRoot = Config.Bind("3. Global Mesh Surgery settings", "ExpandSearchToWeaponRoot", true,
                "Expand the mesh surgery search root all the way up to the Weapon_root node.\n" +
                "When enabled, meshes on the weapon body under Weapon_root are also candidates\n" +
                "for cutting — not just those in the scope sub-hierarchy.\n" +
                "Use this when scope geometry blends into the weapon receiver and you need to cut\n" +
                "the underlying weapon meshes as well.\n" +
                "Example path: Weapon_root/Weapon_root_anim/weapon/mod_scope/...");
            DebugShowHousingMask = Config.Bind("3. Global Mesh Surgery settings", "DebugShowHousingMask", false,
                "Render a red/yellow overlay wherever the scope housing stencil mask is\n" +
                "suppressing the reticle.  Use this to diagnose which meshes are incorrectly\n" +
                "masking the aperture.  Combine with the BepInEx log to see the exact renderer\n" +
                "names printed by CollectHousingRenderers.  Disable in normal play.");
            StencilIncludeWeaponMeshes = Config.Bind("3. Global Mesh Surgery settings", "StencilIncludeWeaponMeshes", true,
                "Include weapon body renderers (found under the 'weapon' transform) in the\n" +
                "stencil mask alongside the scope housing.  Prevents the reticle from\n" +
                "bleeding through the weapon mesh at screen centre.");

            // --- Custom Mesh Surgery settings ---
            SaveCustomMeshSurgerySettingsKey = Config.Bind("4. Custom Mesh Surgery settings", "SaveCustomMeshSurgerySettingsKey", KeyCode.None,
                "When pressed while scoped, save all values from this category for the active scope key into custom_mesh_surgery_settings.json.");
            DeleteCustomMeshSurgerySettingsKey = Config.Bind("4. Custom Mesh Surgery settings", "DeleteCustomMeshSurgerySettingsKey", KeyCode.None,
                "When pressed while scoped, delete custom mesh surgery settings for the active scope key from custom_mesh_surgery_settings.json.");
            CustomPlaneOffsetMeters = Config.Bind("4. Custom Mesh Surgery settings", "PlaneOffsetMeters", 0.001f, "Custom per-scope plane offset applied along plane normal (meters).");
            CustomPlaneNormalAxis = Config.Bind("4. Custom Mesh Surgery settings", "PlaneNormalAxis", "-Y", new ConfigDescription("Custom per-scope local axis for the cut plane normal.", new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z")));
            CustomCutRadius = Config.Bind("4. Custom Mesh Surgery settings", "CutRadius", 0f, new ConfigDescription("Custom per-scope max cut distance in meters (0 = unlimited).", new AcceptableValueRange<float>(0f, 1f)));
            CustomShowCutPlane = Config.Bind("4. Custom Mesh Surgery settings", "ShowCutPlane", false, "Custom per-scope cut plane visualizer toggle.");
            CustomShowCutVolume = Config.Bind("4. Custom Mesh Surgery settings", "ShowCutVolume", false, "Custom per-scope cut volume visualizer toggle.");
            CustomCutVolumeOpacity = Config.Bind("4. Custom Mesh Surgery settings", "CutVolumeOpacity", 0.49f, new ConfigDescription("Custom per-scope cut volume opacity.", new AcceptableValueRange<float>(0.05f, 0.8f)));
            CustomCutMode = Config.Bind("4. Custom Mesh Surgery settings", "CutMode", "Cylinder", new ConfigDescription("Custom per-scope mesh cut mode.", new AcceptableValueList<string>("Plane", "Cylinder")));
            CustomCylinderRadius = Config.Bind("4. Custom Mesh Surgery settings", "CylinderRadius", 0.011f, new ConfigDescription("Custom per-scope near radius in meters.", new AcceptableValueRange<float>(0.001f, 0.1f)));
            CustomMidCylinderRadius = Config.Bind("4. Custom Mesh Surgery settings", "MidCylinderRadius", 0.013f, new ConfigDescription("Custom per-scope mid profile radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomMidCylinderPosition = Config.Bind("4. Custom Mesh Surgery settings", "MidCylinderPosition", 0.28f, new ConfigDescription("Custom per-scope mid profile position (0..1).", new AcceptableValueRange<float>(0.01f, 0.99f)));
            CustomFarCylinderRadius = Config.Bind("4. Custom Mesh Surgery settings", "FarCylinderRadius", 0.12f, new ConfigDescription("Custom per-scope far radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomPlane1OffsetMeters = Config.Bind("4. Custom Mesh Surgery settings", "Plane1OffsetMeters", 0f, new ConfigDescription("Custom per-scope plane 1 offset in meters.", new AcceptableValueRange<float>(-0.02f, 0.02f)));
            CustomPlane2Position = Config.Bind("4. Custom Mesh Surgery settings", "Plane2Position", 0.1138498f, new ConfigDescription("Custom per-scope plane 2 position (0..1), anchored from near when CutLength changes.", new AcceptableValueRange<float>(0f, 1f)));
            CustomPlane2Radius = Config.Bind("4. Custom Mesh Surgery settings", "Plane2Radius", 0.0186338f, new ConfigDescription("Custom per-scope plane 2 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomPlane3Position = Config.Bind("4. Custom Mesh Surgery settings", "Plane3Position", 0.55f, new ConfigDescription("Custom per-scope plane 3 depth (0..1).", new AcceptableValueRange<float>(0f, 1f)));
            CustomPlane3Radius = Config.Bind("4. Custom Mesh Surgery settings", "Plane3Radius", 0.2f, new ConfigDescription("Custom per-scope plane 3 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomPlane4Position = Config.Bind("4. Custom Mesh Surgery settings", "Plane4Position", 1f, new ConfigDescription("Custom per-scope plane 4 depth (0..1).", new AcceptableValueRange<float>(0f, 1f)));
            CustomPlane4Radius = Config.Bind("4. Custom Mesh Surgery settings", "Plane4Radius", 0.2f, new ConfigDescription("Custom per-scope plane 4 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomCutStartOffset = Config.Bind("4. Custom Mesh Surgery settings", "CutStartOffset", 0.04084507f, new ConfigDescription("Custom per-scope cut start offset in meters.", new AcceptableValueRange<float>(-0.2f, 0.2f)));
            CustomCutLength = Config.Bind("4. Custom Mesh Surgery settings", "CutLength", 0.755493f, new ConfigDescription("Custom per-scope cut length in meters.", new AcceptableValueRange<float>(0.01f, 4f)));
            CustomNearPreserveDepth = Config.Bind("4. Custom Mesh Surgery settings", "NearPreserveDepth", 0.02549295f, new ConfigDescription("Custom per-scope near preserve depth in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomShowReticle = Config.Bind("4. Custom Mesh Surgery settings", "ShowReticle", true, "Custom per-scope reticle visibility.");
            CustomReticleBaseSize = Config.Bind("4. Custom Mesh Surgery settings", "ReticleBaseSize", 0.030f, new ConfigDescription("Custom per-scope reticle base diameter in meters.", new AcceptableValueRange<float>(0f, 0.2f)));
            CustomReticleOverlayCamera = Config.Bind("4. Custom Mesh Surgery settings", "ReticleOverlayCamera", true, "Deprecated setting mirrored for per-scope persistence.");
            CustomRestoreOnUnscope = Config.Bind("4. Custom Mesh Surgery settings", "RestoreOnUnscope", true, "Custom per-scope restore behavior when leaving scope.");
            CustomExpandSearchToWeaponRoot = Config.Bind("4. Custom Mesh Surgery settings", "ExpandSearchToWeaponRoot", true, "Custom per-scope search root expansion to Weapon_root.");

            // --- Scope Effects ---
            VignetteEnabled = Config.Bind("5. Scope Effects", "VignetteEnabled", true,
                "Render a circular vignette ring around the scope aperture.\n" +
                "A world-space quad at the lens position fading from transparent centre to black edge.");
            VignetteOpacity = Config.Bind("5. Scope Effects", "VignetteOpacity", 0.39f,
                new ConfigDescription("Maximum opacity of the lens vignette ring (0=invisible, 1=full black).",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSizeMult = Config.Bind("5. Scope Effects", "VignetteSizeMult", 0.35f,
                new ConfigDescription(
                    "Vignette quad diameter as a multiplier of ReticleBaseSize.\n" +
                    "1.0 = same size as reticle.  1.5 gives a visible border ring.\n" +
                    "Higher values (5-15) may be needed for high-magnification scopes.",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSoftness = Config.Bind("5. Scope Effects", "VignetteSoftness", 0.51f,
                new ConfigDescription(
                    "Fraction of the vignette radius used for the gradient falloff (0=hard edge, 1=full gradient).",
                    new AcceptableValueRange<float>(0f, 1f)));

            ScopeShadowEnabled = Config.Bind("5. Scope Effects", "ScopeShadowEnabled", true,
                "Overlay a fullscreen scope-tube shadow: black everywhere except a transparent\n" +
                "circular window in the centre.  Simulates looking down a scope tube.");
            ScopeShadowOpacity = Config.Bind("5. Scope Effects", "ScopeShadowOpacity", 0.75f,
                new ConfigDescription("Maximum opacity of the scope shadow overlay.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ScopeShadowRadius = Config.Bind("5. Scope Effects", "ScopeShadowRadius", 0.07859156f,
                new ConfigDescription(
                    "Radius of the transparent centre window as a fraction of the half-screen (0.0-0.5).\n" +
                    "0.18 = window fills roughly 36% of screen width.  Increase for wider aperture.",
                    new AcceptableValueRange<float>(0.02f, 0.5f)));
            ScopeShadowSoftness = Config.Bind("5. Scope Effects", "ScopeShadowSoftness", 0.08535211f,
                new ConfigDescription(
                    "Width of the gradient edge between the clear window and the black shadow (fraction of screen).\n" +
                    "0 = hard edge.  0.05-0.1 is a natural-looking falloff.",
                    new AcceptableValueRange<float>(0f, 0.3f)));

            // --- Diagnostics ---
            DiagnosticsKey = Config.Bind("6. Diagnostics", "DiagnosticsKey", KeyCode.F8,
                "Press to log full diagnostics for the currently active scope: name, hierarchy,\n" +
                "magnification and cut-plane config.");

            // --- Debug ---
            VerboseLogging = Config.Bind("7. Debug", "VerboseLogging", false,
                "Enable detailed logging. Turn on to diagnose lens/zoom issues.");

            Patches.Patcher.Enable();

            // Initialize scope detection via PWA reflection
            ScopeLifecycle.Init();

            // Initialize zoom system
            ZoomController.LoadShader();

            // --- Config change handlers (catches config manager changes, not just hotkeys) ---
            ModEnabled.SettingChanged += OnModEnabledChanged;
            EnableWeaponScaling.SettingChanged += OnWeaponScalingToggled;
            ScopeWhitelistNames.SettingChanged += OnWhitelistSettingsChanged;

            Logger.LogInfo("ScopeHousingMeshSurgery v4.7.0 loaded.");
            Logger.LogInfo($"  ModEnabled={ModEnabled.Value}  DisablePiP={DisablePiP.Value}  MakeLensesTransparent={MakeLensesTransparent.Value}");
            Logger.LogInfo($"  WhitelistNames='{ScopeWhitelistNames.Value}'");
            Logger.LogInfo($"  EnableZoom={EnableZoom.Value}");
            Logger.LogInfo($"  AutoFov={AutoFovFromScope.Value}  DefaultZoom={DefaultZoom.Value}  FovAnimDur={FovAnimationDuration.Value}s");
            Logger.LogInfo($"  EnableMeshSurgery={EnableMeshSurgery.Value}  CutMode={CutMode.Value}  CutLen={CutLength.Value}  NearPreserve={NearPreserveDepth.Value}  ShowReticle={ShowReticle.Value}  ClearMeshCacheOnRaidEnd={ClearMeshCacheOnRaidEnd.Value}");
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
        internal static bool GetReticleOverlayCamera() => ActiveScopeOverride != null ? ActiveScopeOverride.ReticleOverlayCamera : ReticleOverlayCamera.Value;
        internal static bool GetRestoreOnUnscope() => ActiveScopeOverride != null ? ActiveScopeOverride.RestoreOnUnscope : RestoreOnUnscope.Value;
        internal static bool GetExpandSearchToWeaponRoot() => ActiveScopeOverride != null ? ActiveScopeOverride.ExpandSearchToWeaponRoot : ExpandSearchToWeaponRoot.Value;
        internal static bool GetDebugShowHousingMask() => DebugShowHousingMask?.Value ?? false;

        private void OnDestroy()
        {
            // Plugin unload or game exit — restore everything
            ScopeLifecycle.ForceExit();
            LensTransparency.FullRestoreAll();
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
            bool inRaid = IsInRaid();
            if (ClearMeshCacheOnRaidEnd.Value && _wasInRaid && !inRaid)
            {
                MeshSurgeryManager.ClearPersistentCache();
            }
            _wasInRaid = inRaid;

            // --- Global mod toggle (always active, even when mod is OFF) ---
            if (ModToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModToggleKey.Value))
            {
                ModEnabled.Value = !ModEnabled.Value;
                Logger.LogInfo($"[Global] Mod {(ModEnabled.Value ? "ENABLED" : "DISABLED")}");
                // Cleanup/restore handled by OnModEnabledChanged via SettingChanged
            }

            // When mod is disabled, skip ALL per-frame logic
            if (!ModEnabled.Value) return;

            // --- Feature toggle keys ---
            if (InputProxy.GetKeyDown(MeshSurgeryToggleKey.Value))
            {
                EnableMeshSurgery.Value = !EnableMeshSurgery.Value;
                Logger.LogInfo($"Mesh surgery toggled: {EnableMeshSurgery.Value}");
                if (!EnableMeshSurgery.Value)
                    MeshSurgeryManager.RestoreAll();
            }

            if (InputProxy.GetKeyDown(DisablePiPToggleKey.Value))
            {
                DisablePiP.Value = !DisablePiP.Value;
                Logger.LogInfo($"Disable PiP toggled: {DisablePiP.Value}");
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
                    Logger.LogWarning("[CustomMeshSettings] Save ignored: no active scope key");
                }
                else
                {
                    bool saved = PerScopeMeshSurgerySettings.SaveCustomSettingsForScope(scopeKey);
                    Logger.LogInfo(saved
                        ? $"[CustomMeshSettings] Saved custom settings for scope key '{scopeKey}'"
                        : "[CustomMeshSettings] Save failed");
                }
            }

            if (DeleteCustomMeshSurgerySettingsKey.Value != KeyCode.None && InputProxy.GetKeyDown(DeleteCustomMeshSurgerySettingsKey.Value))
            {
                string scopeKey = ScopeLifecycle.GetActiveScopeWhitelistKey();
                if (string.IsNullOrWhiteSpace(scopeKey))
                {
                    Logger.LogWarning("[CustomMeshSettings] Delete ignored: no active scope key");
                }
                else
                {
                    bool removed = PerScopeMeshSurgerySettings.DeleteCustomSettingsForScope(scopeKey);
                    Logger.LogInfo(removed
                        ? $"[CustomMeshSettings] Deleted custom settings for scope key '{scopeKey}'"
                        : $"[CustomMeshSettings] No custom settings existed for scope key '{scopeKey}'");
                }
            }

            if (InputProxy.GetKeyDown(LensesTransparentToggleKey.Value))
            {
                MakeLensesTransparent.Value = !MakeLensesTransparent.Value;
                Logger.LogInfo($"Lens transparency toggled: {MakeLensesTransparent.Value}");
                if (!MakeLensesTransparent.Value)
                    LensTransparency.RestoreAll();
            }

            if (ZoomToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ZoomToggleKey.Value))
            {
                EnableZoom.Value = !EnableZoom.Value;
                Logger.LogInfo($"Zoom toggled: {EnableZoom.Value}");
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
