using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace PiPDisabler
{
    internal static class ModSettings
    {
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

        internal static void Bind(BaseUnityPlugin plugin)
        {
        // --- 0. Global ---
            ModEnabled = plugin.Config.Bind("0. Global", "ModEnabled", true,
                "Master ON/OFF switch for the entire mod. When OFF, all effects are " +
                "cleaned up and the game behaves as if the mod is not installed.");
            ModToggleKey = plugin.Config.Bind("0. Global", "ModToggleKey", KeyCode.Backspace,
                new ConfigDescription(
                    "Toggle key for master mod enable/disable.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoSwitchReticleRenderForNvg = plugin.Config.Bind("0. Global", "AutoSwitchReticleRenderForNvg", true,
                new ConfigDescription(
                    "Automatically switch reticle CommandBuffer event based on NVG state.\n" +
                    "ON: use AfterForwardAlpha while NVG are active and AfterEverything otherwise.\n" +
                    "OFF: keep the normal path (AfterForwardAlpha) unless debug override is enabled.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- General ---
            DisablePiP = plugin.Config.Bind("1. General", "DisablePiP", true,
                new ConfigDescription(
                    "Disable Picture-in-Picture optic rendering (No-PiP mode). " +
                    "Core feature — gives identical perf between hip-fire and ADS.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoDisableForVariableScopes = plugin.Config.Bind("1. General", "AutoDisableForVariableScopes", true,
                new ConfigDescription(
                    "Automatically disable all mod effects while scoped with variable magnification optics (IsAdjustableOptic=true).\n" +
                    "Also bypasses thermal/night-vision scopes detected via ScopeData.ThermalVisionData or NightVisionData.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoBypassNameContains = plugin.Config.Bind("1. General", "AutoBypassNameContains", "npz, PU, vomz, d-evo",
                new ConfigDescription(
                    "Comma-separated list of substrings. Any scope whose object name or scope key contains one of these ",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopeWhitelistNames = plugin.Config.Bind("1. General", "ScopeWhitelistNames", "",
                "Comma/semicolon/newline separated list of allowed scope keys.\n" +
                "Primary key is derived from the object under mod_scope that does not contain mount (case-insensitive).\n" +
                "Fallbacks: template _name, template _id, then optic object name. Empty list = whitelist ignored.");
            ScopeWhitelistToggleEntryKey = plugin.Config.Bind("1. General", "ScopeWhitelistToggleEntryKey", KeyCode.None,
                "When pressed while scoped, add/remove the current scope key in ScopeWhitelistNames (derived from mod_scope non-mount object).");
            DisablePiPToggleKey = plugin.Config.Bind("1. General", "DisablePiPToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for PiP disable.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            MakeLensesTransparent = plugin.Config.Bind("1. General", "MakeLensesTransparent", true,
                new ConfigDescription(
                    "Hide lens surfaces (linza/backLens) while scoped so you see through the tube.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            LensesTransparentToggleKey = plugin.Config.Bind("1. General", "LensesTransparentToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for lens transparency.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BlackLensWhenUnscoped = plugin.Config.Bind("1. General", "BlackLensWhenUnscoped", true,
                new ConfigDescription(
                    "When unscoping, apply a solid black opaque material to the lens instead of restoring " +
                    "the original PiP/sight material. Eliminates the reticle flash during the unscope " +
                    "transition and gives the scope a realistic dark-glass appearance when not in use.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Weapon Scaling ---
            EnableWeaponScaling = plugin.Config.Bind("2. Zoom", "EnableWeaponScaling", true,
                new ConfigDescription(
                    "Compensate weapon/arms model scale across magnification levels.\n" +
                    "Without this, zooming in (lower FOV) makes the weapon appear larger on screen.\n" +
                    "With this enabled, the weapon shrinks proportionally as you zoom in so it\n" +
                    "always occupies the same screen space at every magnification level.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BaselineWeaponScale = plugin.Config.Bind("2. Zoom", "BaselineWeaponScale", 0.9624413f,
                new ConfigDescription(
                    "Base weapon scale applied at all FOV values before compensation.\n" +
                    "1.00 = default EFT visual scale.",
                    new AcceptableValueRange<float>(0.00f, 2.00f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            WeaponScaleStrength = plugin.Config.Bind("2. Zoom", "WeaponScaleStrength", 0.2723005f,
                new ConfigDescription(
                    "Blends between no compensation and full inverse-FOV compensation.\n" +
                    "0.00 = no compensation, 1.00 = full compensation, values outside [0,1] over/under-compensate.",
                    new AcceptableValueRange<float>(-2.00f, 2.00f) , new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Zoom ---
            EnableZoom = plugin.Config.Bind("2. Zoom", "EnableZoom", true,
                new ConfigDescription(
                    "Enable scope magnification via FOV zoom.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            DefaultZoom = plugin.Config.Bind("2. Zoom", "DefaultZoom", 4f,
                new ConfigDescription(
                    "Default magnification when auto-detection fails (e.g. fixed scopes without zoom data).",
                    new AcceptableValueRange<float>(1f, 16f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoFovFromScope = plugin.Config.Bind("2. Zoom", "AutoFovFromScope", true,
                new ConfigDescription(
                    "Auto-detect magnification from the scope's zoom data (ScopeZoomHandler). " +
                    "Works for variable-zoom scopes. Falls back to DefaultZoom for fixed scopes.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopedFov = plugin.Config.Bind("2. Zoom", "ScopedFov", 15f,
                new ConfigDescription(
                    "FOV (degrees) for FOV zoom fallback mode. Lower = more zoom. " +
                    "Used for FOV zoom.",
                    new AcceptableValueRange<float>(5f, 75f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            FovAnimationDuration = plugin.Config.Bind("2. Zoom", "FovAnimationDuration", 1f,
                new ConfigDescription(
                    "Duration (seconds) of the FOV zoom-in animation when entering ADS.\n" +
                    "0 = instant snap. 0.25 = smooth quarter-second transition.\n" +
                    "Scope exit always restores FOV instantly to avoid sluggish feel.",
                    new AcceptableValueRange<float>(0f, 2f)));

            ManualLodBias = plugin.Config.Bind("2. Zoom", "ManualLodBias", 4.0f,
                new ConfigDescription(
                    "Manual LOD bias while scoped.\n" +
                    "0 = auto (baseLodBias * magnification).\n" +
                    ">0 = force this exact value (e.g. 4.0).",
                    new AcceptableValueRange<float>(0f, 20f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            ManualMaximumLodLevel = plugin.Config.Bind("2. Zoom", "ManualMaximumLodLevel", -1,
                new ConfigDescription(
                    "Manual QualitySettings.maximumLODLevel while scoped.\n" +
                    "-1 = auto (force 0 / highest detail).\n" +
                    ">=0 = force this exact max LOD level.",
                    new AcceptableValueRange<int>(-1, 8) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            ManualCullingMultiplier = plugin.Config.Bind("2. Zoom", "ManualCullingMultiplier", 0.8f,
                new ConfigDescription(
                    "Manual multiplier for Camera.layerCullDistances while scoped.\n" +
                    "0 = auto (use magnification).\n" +
                    ">0 = force this multiplier (e.g. 2.0 doubles cull distances).",
                    new AcceptableValueRange<float>(0f, 20f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            MapManualLodBias = new Dictionary<string, ConfigEntry<float>>(StringComparer.OrdinalIgnoreCase);

            BindPerMapLodBias(plugin, "Woods", "Woods", "Woods");
            BindPerMapLodBias(plugin, "Factory", "Factory", "factory4_day", "factory4_night");
            BindPerMapLodBias(plugin, "Customs", "Customs", "bigmap");
            BindPerMapLodBias(plugin, "Shoreline", "Shoreline", "Shoreline");
            BindPerMapLodBias(plugin, "Interchange", "Interchange", "Interchange");
            BindPerMapLodBias(plugin, "Reserve", "Reserve", "RezervBase");
            BindPerMapLodBias(plugin, "TheLab", "The Lab", "laboratory");
            BindPerMapLodBias(plugin, "Lighthouse", "Lighthouse", "Lighthouse");
            BindPerMapLodBias(plugin, "StreetsOfTarkov", "Streets of Tarkov", "TarkovStreets");
            BindPerMapLodBias(plugin, "GroundZero", "Ground Zero", "Sandbox", "Sandbox_high");
            ZoomToggleKey = plugin.Config.Bind("2. Zoom", "ZoomToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for zoom (None = always on when EnableZoom is true).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- 4. Zeroing ---
            EnableZeroing = plugin.Config.Bind("4. Zeroing", "EnableZeroing", true,
                new ConfigDescription(
                    "Enable optic zeroing (calibration distance adjustment) via keyboard.\n" +
                    "Uses the proper EFT pathway (works with Fika).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ZeroingUpKey = plugin.Config.Bind("4. Zeroing", "ZeroingUpKey", KeyCode.PageUp,
                new ConfigDescription(
                    "Key to increase zeroing distance.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ZeroingDownKey = plugin.Config.Bind("4. Zeroing", "ZeroingDownKey", KeyCode.PageDown,
                new ConfigDescription(
                    "Key to decrease zeroing distance.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Mesh Surgery (ON by default, Cylinder mode) ---
            EnableMeshSurgery = plugin.Config.Bind("3. Global Mesh Surgery settings", "EnableMeshSurgery", true,
                new ConfigDescription(
                    "Enable runtime mesh cutting to bore a hole through the scope housing.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            MeshSurgeryToggleKey = plugin.Config.Bind("3. Global Mesh Surgery settings", "MeshSurgeryToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for mesh surgery.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            RestoreOnUnscope = plugin.Config.Bind("3. Global Mesh Surgery settings", "RestoreOnUnscope", true,
                new ConfigDescription(
                    "Restore original meshes when leaving scope.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            PlaneOffsetMeters = plugin.Config.Bind("3. Global Mesh Surgery settings", "PlaneOffsetMeters", 0.001f,
                new ConfigDescription(
                    "Offset applied along plane normal (meters).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            PlaneNormalAxis = plugin.Config.Bind("3. Global Mesh Surgery settings", "PlaneNormalAxis", "-Y",
                new ConfigDescription(
                    "Which local axis to use as the cut plane normal.\n" +
                    "Auto = use backLens.forward (game default).\n" +
                    "X/Y/Z = force that local axis as the plane normal.\n" +
                    "-X/-Y/-Z = force the negative of that axis.\n" +
                    "If the cut is horizontal when it should be vertical, try Z or Y.",
                    new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z") , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutRadius = plugin.Config.Bind("3. Global Mesh Surgery settings", "CutRadius", 0f,
                new ConfigDescription(
                    "Max distance (meters) from scope center to cut. 0 = unlimited (cut all geometry).\n" +
                    "Set to e.g. 0.05 to only cut geometry near the lens opening.",
                    new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            ShowCutPlane = plugin.Config.Bind("3. Global Mesh Surgery settings", "ShowCutPlane", false, new ConfigDescription("Show green/red semi-transparent circles at the near/far cut plane positions.\n" +
                "Use this to visualize the cut endpoints.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            ShowCutVolume = plugin.Config.Bind("3. Global Mesh Surgery settings", "ShowCutVolume", false, new ConfigDescription("Show a semi-transparent 3D tube representing the full cut volume.\n" +
                "Visualizes the near→mid→far radius profile so you can see exactly what gets removed.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutVolumeOpacity = plugin.Config.Bind("3. Global Mesh Surgery settings", "CutVolumeOpacity", 0.49f,
                new ConfigDescription(
                    "Opacity of the 3D cut volume visualizer (0 = invisible, 1 = opaque).",
                    new AcceptableValueRange<float>(0.05f, 0.8f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutMode = plugin.Config.Bind("3. Global Mesh Surgery settings", "CutMode", "Cylinder",
                new ConfigDescription(
                    "Plane = flat infinite cut. Cylinder = cylindrical bore cut centered on the lens axis.\n" +
                    "Cylinder removes geometry inside a cylinder of CylinderRadius around the lens center.",
                    new AcceptableValueList<string>("Plane", "Cylinder") , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CylinderRadius = plugin.Config.Bind("3. Global Mesh Surgery settings", "CylinderRadius", 0.011f,
                new ConfigDescription(
                    "Near radius (meters) of the cylindrical/cone cut (camera side).\n" +
                    "Typical scope lens radius is ~0.01-0.02m.",
                    new AcceptableValueRange<float>(0.001f, 0.1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            MidCylinderRadius = plugin.Config.Bind("3. Global Mesh Surgery settings", "MidCylinderRadius", 0.013f,
                new ConfigDescription(
                    "Intermediate radius (meters) at MidCylinderPosition along the bore.\n" +
                    "0 = disabled (linear near→far interpolation).\n" +
                    ">0 = two-segment interpolation: near→mid, then mid→far.\n" +
                    "Set smaller than near/far to create a waist (hourglass). Set larger for a bulge.",
                    new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            MidCylinderPosition = plugin.Config.Bind("3. Global Mesh Surgery settings", "MidCylinderPosition", 0.28f,
                new ConfigDescription(
                    "Position of the mid-radius control point along the cut length (0=near, 1=far).\n" +
                    "0.5 = midpoint. 0.3 = closer to camera. 0.7 = closer to objective.",
                    new AcceptableValueRange<float>(0.01f, 0.99f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            FarCylinderRadius = plugin.Config.Bind("3. Global Mesh Surgery settings", "FarCylinderRadius", 0.12f,
                new ConfigDescription(
                    "Far radius (meters) of the cone cut (objective side).\n" +
                    "0 = same as CylinderRadius (pure cylinder). >0 creates a cone/frustum shape.\n" +
                    "Set larger than CylinderRadius to widen the bore toward the objective lens.",
                    new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane1OffsetMeters = plugin.Config.Bind("3. Global Mesh Surgery settings", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Offset (meters) for plane 1 from linza/backLens origin along bore axis.\n" +
                    "Plane 1 radius is always CylinderRadius.",
                    new AcceptableValueRange<float>(-0.02f, 0.02f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane2Position = plugin.Config.Bind("3. Global Mesh Surgery settings", "Plane2Position", 0.1138498f,
                new ConfigDescription(
                    "Plane 2 profile position (0..1) anchored from the near side.\n" +
                    "Changing CutLength keeps this plane at the same world-space depth from near.",
                    new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane2Radius = plugin.Config.Bind("3. Global Mesh Surgery settings", "Plane2Radius", 0.0186338f,
                new ConfigDescription(
                    "Radius (meters) at plane 2.",
                    new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane3Position = plugin.Config.Bind("3. Global Mesh Surgery settings", "Plane3Position", 0.55f,
                new ConfigDescription(
                    "Normalized depth of plane 3 from near (0) to far (1).",
                    new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane3Radius = plugin.Config.Bind("3. Global Mesh Surgery settings", "Plane3Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 3.",
                    new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane4Position = plugin.Config.Bind("3. Global Mesh Surgery settings", "Plane4Position", 1f,
                new ConfigDescription(
                    "Normalized depth of plane 4 from near (0) to far (1). Usually 1.",
                    new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane4Radius = plugin.Config.Bind("3. Global Mesh Surgery settings", "Plane4Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 4 (away from player).",
                    new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutStartOffset = plugin.Config.Bind("3. Global Mesh Surgery settings", "CutStartOffset", 0.04084507f,
                new ConfigDescription(
                    "How far behind the backLens (toward the camera) the near cut plane starts.\n" +
                    "The near plane is fixed at: backLens - (offset × boreAxis).\n" +
                    "0 = starts exactly at the backLens. 0.05 = 5cm behind it (catches interior tube geometry).\n" +
                    "Changing CutLength does NOT move this plane.",
                    new AcceptableValueRange<float>(0f, 0.3f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutLength = plugin.Config.Bind("3. Global Mesh Surgery settings", "CutLength", 0.755493f,
                new ConfigDescription(
                    "How far forward from the near plane the cut extends (toward the objective).\n" +
                    "The far plane is at: nearPlane + (length × boreAxis).\n" +
                    "Only the far plane moves when you change this value.",
                    new AcceptableValueRange<float>(0.01f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            NearPreserveDepth = plugin.Config.Bind("3. Global Mesh Surgery settings", "NearPreserveDepth", 0.02549295f,
                new ConfigDescription(
                    "Depth (meters) from the near cut plane where NO geometry is cut.\n" +
                    "Preserves the eyepiece housing closest to the camera so you\n" +
                    "keep a thin ring framing the reticle while ADSing.\n" +
                    "0.01 = 1cm ring (default). 0.02-0.04 = thicker ring.\n" +
                    "0 = disabled (cut starts at the near plane — removes everything).",
                    new AcceptableValueRange<float>(0f, 0.15f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            ShowReticle = plugin.Config.Bind("3. Global Mesh Surgery settings", "ShowReticle", true, new ConfigDescription("Render the scope reticle texture as a glowing overlay where the lens was.\n" +
                "Uses alpha blending so the reticle's own alpha channel controls transparency.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            ReticleBaseSize = plugin.Config.Bind("3. Global Mesh Surgery settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Physical diameter (meters) of the reticle quad at 1x magnification.\n" +
                    "The quad is scaled by 1/magnification so screen-pixel coverage stays constant\n" +
                    "across all zoom levels.  Typical scope lens diameter is 0.02-0.04 m.\n" +
                    "Set to 0 to fall back to the legacy CylinderRadius x2 value.",
                    new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            ExpandSearchToWeaponRoot = plugin.Config.Bind("3. Global Mesh Surgery settings", "ExpandSearchToWeaponRoot", true, new ConfigDescription("Expand the mesh surgery search root all the way up to the Weapon_root node.\n" +
                "When enabled, meshes on the weapon body under Weapon_root are also candidates\n" +
                "for cutting — not just those in the scope sub-hierarchy.\n" +
                "Use this when scope geometry blends into the weapon receiver and you need to cut\n" +
                "the underlying weapon meshes as well.\n" +
                "Example path: Weapon_root/Weapon_root_anim/weapon/mod_scope/...", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            DebugShowHousingMask = plugin.Config.Bind("3. Global Mesh Surgery settings", "DebugShowHousingMask", false, new ConfigDescription("Render a red/yellow overlay wherever the scope housing stencil mask is\n" +
                "suppressing the reticle.  Use this to diagnose which meshes are incorrectly\n" +
                "masking the aperture.  Combine with the BepInEx log to see the exact renderer\n" +
                "names printed by CollectHousingRenderers.  Disable in normal play.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            StencilIncludeWeaponMeshes = plugin.Config.Bind("3. Global Mesh Surgery settings", "StencilIncludeWeaponMeshes", true, new ConfigDescription("Include weapon body renderers (found under the 'weapon' transform) in the\n" +
                "stencil mask alongside the scope housing.  Prevents the reticle from\n" +
                "bleeding through the weapon mesh at screen centre.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Custom Mesh Surgery settings ---
            SaveCustomMeshSurgerySettingsKey = plugin.Config.Bind("4. Custom Mesh Surgery settings", "SaveCustomMeshSurgerySettingsKey", KeyCode.None, new ConfigDescription("When pressed while scoped, save all values from this category for the active scope key into custom_mesh_surgery_settings.json.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            DeleteCustomMeshSurgerySettingsKey = plugin.Config.Bind("4. Custom Mesh Surgery settings", "DeleteCustomMeshSurgerySettingsKey", KeyCode.None, new ConfigDescription("When pressed while scoped, delete custom mesh surgery settings for the active scope key from custom_mesh_surgery_settings.json.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlaneOffsetMeters = plugin.Config.Bind("4. Custom Mesh Surgery settings", "PlaneOffsetMeters", 0.001f, new ConfigDescription("Custom per-scope plane offset applied along plane normal (meters).", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlaneNormalAxis = plugin.Config.Bind("4. Custom Mesh Surgery settings", "PlaneNormalAxis", "-Y", new ConfigDescription("Custom per-scope local axis for the cut plane normal.", new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z") , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutRadius = plugin.Config.Bind("4. Custom Mesh Surgery settings", "CutRadius", 0f, new ConfigDescription("Custom per-scope max cut distance in meters (0 = unlimited).", new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomShowCutPlane = plugin.Config.Bind("4. Custom Mesh Surgery settings", "ShowCutPlane", false, new ConfigDescription("Custom per-scope cut plane visualizer toggle.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomShowCutVolume = plugin.Config.Bind("4. Custom Mesh Surgery settings", "ShowCutVolume", false, new ConfigDescription("Custom per-scope cut volume visualizer toggle.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutVolumeOpacity = plugin.Config.Bind("4. Custom Mesh Surgery settings", "CutVolumeOpacity", 0.49f, new ConfigDescription("Custom per-scope cut volume opacity.", new AcceptableValueRange<float>(0.05f, 0.8f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutMode = plugin.Config.Bind("4. Custom Mesh Surgery settings", "CutMode", "Cylinder", new ConfigDescription("Custom per-scope mesh cut mode.", new AcceptableValueList<string>("Plane", "Cylinder") , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCylinderRadius = plugin.Config.Bind("4. Custom Mesh Surgery settings", "CylinderRadius", 0.011f, new ConfigDescription("Custom per-scope near radius in meters.", new AcceptableValueRange<float>(0.001f, 0.1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomMidCylinderRadius = plugin.Config.Bind("4. Custom Mesh Surgery settings", "MidCylinderRadius", 0.013f, new ConfigDescription("Custom per-scope mid profile radius in meters.", new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomMidCylinderPosition = plugin.Config.Bind("4. Custom Mesh Surgery settings", "MidCylinderPosition", 0.28f, new ConfigDescription("Custom per-scope mid profile position (0..1).", new AcceptableValueRange<float>(0.01f, 0.99f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomFarCylinderRadius = plugin.Config.Bind("4. Custom Mesh Surgery settings", "FarCylinderRadius", 0.12f, new ConfigDescription("Custom per-scope far radius in meters.", new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane1OffsetMeters = plugin.Config.Bind("4. Custom Mesh Surgery settings", "Plane1OffsetMeters", 0f, new ConfigDescription("Custom per-scope plane 1 offset in meters.", new AcceptableValueRange<float>(-0.02f, 0.02f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane2Position = plugin.Config.Bind("4. Custom Mesh Surgery settings", "Plane2Position", 0.1138498f, new ConfigDescription("Custom per-scope plane 2 position (0..1), anchored from near when CutLength changes.", new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane2Radius = plugin.Config.Bind("4. Custom Mesh Surgery settings", "Plane2Radius", 0.0186338f, new ConfigDescription("Custom per-scope plane 2 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane3Position = plugin.Config.Bind("4. Custom Mesh Surgery settings", "Plane3Position", 0.55f, new ConfigDescription("Custom per-scope plane 3 depth (0..1).", new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane3Radius = plugin.Config.Bind("4. Custom Mesh Surgery settings", "Plane3Radius", 0.2f, new ConfigDescription("Custom per-scope plane 3 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane4Position = plugin.Config.Bind("4. Custom Mesh Surgery settings", "Plane4Position", 1f, new ConfigDescription("Custom per-scope plane 4 depth (0..1).", new AcceptableValueRange<float>(0f, 1f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane4Radius = plugin.Config.Bind("4. Custom Mesh Surgery settings", "Plane4Radius", 0.2f, new ConfigDescription("Custom per-scope plane 4 radius in meters.", new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutStartOffset = plugin.Config.Bind("4. Custom Mesh Surgery settings", "CutStartOffset", 0.04084507f, new ConfigDescription("Custom per-scope cut start offset in meters.", new AcceptableValueRange<float>(-0.2f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutLength = plugin.Config.Bind("4. Custom Mesh Surgery settings", "CutLength", 0.755493f, new ConfigDescription("Custom per-scope cut length in meters.", new AcceptableValueRange<float>(0.01f, 4f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomNearPreserveDepth = plugin.Config.Bind("4. Custom Mesh Surgery settings", "NearPreserveDepth", 0.02549295f, new ConfigDescription("Custom per-scope near preserve depth in meters.", new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomShowReticle = plugin.Config.Bind("4. Custom Mesh Surgery settings", "ShowReticle", true, new ConfigDescription("Custom per-scope reticle visibility.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomReticleBaseSize = plugin.Config.Bind("4. Custom Mesh Surgery settings", "ReticleBaseSize", 0.030f, new ConfigDescription("Custom per-scope reticle base diameter in meters.", new AcceptableValueRange<float>(0f, 0.2f) , new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomRestoreOnUnscope = plugin.Config.Bind("4. Custom Mesh Surgery settings", "RestoreOnUnscope", true, new ConfigDescription("Custom per-scope restore behavior when leaving scope.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomExpandSearchToWeaponRoot = plugin.Config.Bind("4. Custom Mesh Surgery settings", "ExpandSearchToWeaponRoot", true, new ConfigDescription("Custom per-scope search root expansion to Weapon_root.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Scope Effects ---
            VignetteEnabled = plugin.Config.Bind("5. Scope Effects", "VignetteEnabled", true,
                "Render a circular vignette ring around the scope aperture.\n" +
                "A world-space quad at the lens position fading from transparent centre to black edge.");
            VignetteOpacity = plugin.Config.Bind("5. Scope Effects", "VignetteOpacity", 0.39f,
                new ConfigDescription("Maximum opacity of the lens vignette ring (0=invisible, 1=full black).",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSizeMult = plugin.Config.Bind("5. Scope Effects", "VignetteSizeMult", 0.35f,
                new ConfigDescription(
                    "Vignette quad diameter as a multiplier of ReticleBaseSize.\n" +
                    "1.0 = same size as reticle.  1.5 gives a visible border ring.\n" +
                    "Higher values (5-15) may be needed for high-magnification scopes.",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSoftness = plugin.Config.Bind("5. Scope Effects", "VignetteSoftness", 0.51f,
                new ConfigDescription(
                    "Fraction of the vignette radius used for the gradient falloff (0=hard edge, 1=full gradient).",
                    new AcceptableValueRange<float>(0f, 1f)));

            ScopeShadowEnabled = plugin.Config.Bind("5. Scope Effects", "ScopeShadowEnabled", true,
                "Overlay a fullscreen scope-tube shadow: black everywhere except a transparent\n" +
                "circular window in the centre.  Simulates looking down a scope tube.");
            ScopeShadowOpacity = plugin.Config.Bind("5. Scope Effects", "ScopeShadowOpacity", 0.75f,
                new ConfigDescription("Maximum opacity of the scope shadow overlay.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ScopeShadowRadius = plugin.Config.Bind("5. Scope Effects", "ScopeShadowRadius", 0.07859156f,
                new ConfigDescription(
                    "Radius of the transparent centre window as a fraction of the half-screen (0.0-0.5).\n" +
                    "0.18 = window fills roughly 36% of screen width.  Increase for wider aperture.",
                    new AcceptableValueRange<float>(0.02f, 0.5f)));
            ScopeShadowSoftness = plugin.Config.Bind("5. Scope Effects", "ScopeShadowSoftness", 0.08535211f,
                new ConfigDescription(
                    "Width of the gradient edge between the clear window and the black shadow (fraction of screen).\n" +
                    "0 = hard edge.  0.05-0.1 is a natural-looking falloff.",
                    new AcceptableValueRange<float>(0f, 0.3f)));

            // --- Diagnostics ---
            DiagnosticsKey = plugin.Config.Bind("6. Diagnostics", "DiagnosticsKey", KeyCode.None,
                "Press to log full diagnostics for the currently active scope: name, hierarchy,\n" +
                "magnification and cut-plane config.");

            // --- Debug ---
            VerboseLogging = plugin.Config.Bind("7. Debug", "VerboseLogging", false,
                "Enable detailed logging. Turn on to diagnose lens/zoom issues.");
            DebugLogCutCandidates = plugin.Config.Bind("7. Debug", "DebugLogCutCandidates", false,
                "When enabled, logs every mesh candidate found by mesh surgery (path, mesh name, vertices, active state), " +
                "plus per-candidate radius checks. Useful to diagnose attachments that are not being cut.");
            DebugReticleAfterEverything = plugin.Config.Bind("7. Debug", "DebugReticleAfterEverything", false,
                "Debug toggle for reticle CommandBuffer event. False = AfterForwardAlpha (default, NVG-friendly). " +
                "True = AfterEverything (late overlay testing). ");
        }

        private static void BindPerMapLodBias(BaseUnityPlugin plugin, string configKeySuffix, string mapDisplayName, params string[] locationIds)
        {
            if (locationIds == null || locationIds.Length == 0 || string.IsNullOrWhiteSpace(configKeySuffix))
                return;

            var entry = plugin.Config.Bind("2. Zoom Per-Map", $"ManualLodBias_{configKeySuffix}", ManualLodBias.Value,
                new ConfigDescription(
                    $"Manual LOD bias override while scoped on map '{mapDisplayName}'.\n" +
                    "0 = auto (baseLodBias * magnification).\n" +
                    ">0 = force this exact value (e.g. 4.0).",
                    new AcceptableValueRange<float>(0f, 20f)));

            for (int i = 0; i < locationIds.Length; i++)
            {
                string locationId = locationIds[i];
                if (string.IsNullOrWhiteSpace(locationId))
                    continue;
                MapManualLodBias[locationId] = entry;
            }
        }
    }
}
