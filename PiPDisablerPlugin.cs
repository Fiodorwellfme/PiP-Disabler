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
    [BepInPlugin("com.fiodor.pipdisabler", "PiP-Disabler", "0.7.0")]
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

        // --- Mesh Surgery ---
        internal static ConfigEntry<bool> EnableMeshSurgery;
        internal static ConfigEntry<KeyCode> MeshSurgeryToggleKey;
        internal static ConfigEntry<float> ReloadMeshResumeDelaySeconds;
        internal static ConfigEntry<float> PlaneOffsetMeters;
        internal static ConfigEntry<string> PlaneNormalAxis;
        internal static ConfigEntry<float> Plane1Radius;
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
        internal static ConfigEntry<float> ReticleBaseSize;
        internal static ConfigEntry<bool> ExpandSearchToWeaponRoot;
        internal static ConfigEntry<bool> DebugShowHousingMask;
        internal static ConfigEntry<bool> StencilIncludeWeaponMeshes;

        // --- Custom Mesh Surgery settings (per-scope authoring) ---
        internal static ConfigEntry<KeyCode> SaveCustomMeshSurgerySettingsKey;
        internal static ConfigEntry<KeyCode> DeleteCustomMeshSurgerySettingsKey;
        internal static ConfigEntry<float> CustomPlaneOffsetMeters;
        internal static ConfigEntry<float> CustomCutRadius;
        internal static ConfigEntry<float> CustomPlane1Radius;
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
        internal static ConfigEntry<float> CustomReticleBaseSize;
        internal static ConfigEntry<bool> CustomExpandSearchToWeaponRoot;

        // --- Scope Effects ---
        internal static ConfigEntry<bool>  VignetteEnabled;
        internal static ConfigEntry<float> VignetteOpacity;
        internal static ConfigEntry<float> VignetteSizeMult;
        internal static ConfigEntry<float> VignetteSoftness;
        internal static ConfigEntry<bool>  ScopeShadowEnabled;
        internal static ConfigEntry<bool>  DebugShowScopeShadowMask;
        internal static ConfigEntry<bool>  ScopeShadowPersistOnUnscope;
        internal static ConfigEntry<float> ScopeShadowOpacity;
        internal static ConfigEntry<float> ScopeShadowRadius;
        internal static ConfigEntry<float> ScopeShadowSoftness;

        // --- Weapon Scaling ---
        internal static ConfigEntry<bool> EnableWeaponScaling;
        internal static ConfigEntry<float> BaselineWeaponScale;
        internal static ConfigEntry<float> WeaponScaleStrength;
        // --- Zoom / FOV ---
        internal static ConfigEntry<bool> EnableZoom;
        internal static ConfigEntry<float> DefaultZoom;
        internal static ConfigEntry<float> BaselineFOV;
        internal static ConfigEntry<bool> AutoFovFromScope;
        internal static ConfigEntry<float> ScopedFov;
        internal static ConfigEntry<float> FovAnimationDuration;
        internal static ConfigEntry<KeyCode> ZoomToggleKey;
        internal static ConfigEntry<float> ManualLodBias;
        internal static ConfigEntry<int> ManualMaximumLodLevel;
        internal static ConfigEntry<float> ManualCullingMultiplier;
        internal static Dictionary<string, ConfigEntry<float>> MapManualLodBias;

        // --- Debug ---
        internal static ConfigEntry<bool> VerboseLogging;
        internal static ConfigEntry<bool> DebugLogCutCandidates;
        internal static ConfigEntry<bool> DebugMeshSurgeryLifecycle;
        internal static ConfigEntry<bool> DebugReticleAfterEverything;


        private void Awake()
        {
            Instance = this;

            // --- 0. Global ---
            ModEnabled = Config.Bind("Global", "ModEnabled", true,
                new ConfigDescription(
                    "Master ON/OFF switch for the entire mod.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false, Order = 2 }));
            ModToggleKey = Config.Bind("Global", "ModToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for master mod enable/disable.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false, Order = 1 }));
            AutoSwitchReticleRenderForNvg = Config.Bind("General", "AutoSwitchReticleRenderForNvg", true,
                new ConfigDescription(
                    "Automatically switch reticle CommandBuffer event based on NVG state.\n" +
                "ON: use AfterForwardAlpha while NVG are active and AfterEverything otherwise.\n" +
                "OFF: keep the normal path (AfterForwardAlpha) unless debug override is enabled.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Order = 0 }));

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
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoBypassNameContains = Config.Bind("General", "AutoBypassNameContains", "npz, PU, vomz, d-evo",
                new ConfigDescription(
                    "Comma-separated list of substrings. Any scope whose object name or scope key contains one of these ",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
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
            // --- Weapon Scaling ---
            EnableWeaponScaling = Config.Bind("General", "EnableWeaponScaling", true,
                new ConfigDescription(
                    "Compensate weapon/arms model scale across magnification levels.\n" +
                "Without this, zooming in (lower FOV) makes the weapon appear larger on screen.\n" +
                "With this enabled, the weapon shrinks proportionally as you zoom in so it\n" +
                "always occupies the same screen space at every magnification level.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BaselineWeaponScale = Config.Bind("General", "BaselineWeaponScale", 0.8873239f,
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
            BaselineFOV = Config.Bind("General", "BaselineFOV", 35f,
                new ConfigDescription(
                    "What to use when calculating magnified FOV (if set to 50 then 2x will be 25°).\n" +
                    "Be aware that 1x is always forced to 35°.",
                    new AcceptableValueRange<float>(20f, 50f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            AutoFovFromScope = Config.Bind("General", "AutoFovFromScope", true, //To remove
                new ConfigDescription(
                    "Auto-detect magnification from the scope's zoom data (ScopeZoomHandler). " +
                "Works for variable-zoom scopes. Falls back to DefaultZoom for fixed scopes.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopedFov = Config.Bind("General", "ScopedFov", 15f,  //To remove
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

            ManualLodBias = Config.Bind("Optimization", "ManualLodBias", 4.0f,  //Should be gated behind a toggle, otherwise default to ScopeCameraData
                new ConfigDescription(
                    "Manual LOD bias while scoped.\n" +
                    "0 = auto (baseLodBias * magnification).\n" +
                    ">0 = force this exact value (e.g. 4.0).",
                    new AcceptableValueRange<float>(0f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ManualMaximumLodLevel = Config.Bind("Optimization", "ManualMaximumLodLevel", -1, //To remove, useless
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
            ZoomToggleKey = Config.Bind("General", "ZoomToggleKey", KeyCode.None, //To remove
                new ConfigDescription(
                    "Toggle key for zoom (None = always on when EnableZoom is true).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Mesh Surgery (ON by default, Cylinder mode) --- //Needs global cleanup, config name standardization.
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
            ReloadMeshResumeDelaySeconds = Config.Bind("Global Mesh Surgery settings", "ReloadMeshResumeDelaySeconds", 0.12f, //Needs renaming, should be in general
                new ConfigDescription(
                    "Extra delay in seconds after reload ends before mesh surgery re-applies/reticle displays etc....",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false, ShowRangeAsPercent = false}));
            PlaneOffsetMeters = Config.Bind("Global Mesh Surgery settings", "PlaneOffsetMeters", 0.001f,
                new ConfigDescription(
                    "Offset applied along plane normal (meters).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            PlaneNormalAxis = Config.Bind("Global Mesh Surgery settings", "PlaneNormalAxis", "-Y", //Should be hard set to -Y
                new ConfigDescription(
                    "Which local axis to use as the cut plane normal.\n" +
                    "Auto = use backLens.forward (game default).\n" +
                    "X/Y/Z = force that local axis as the plane normal.\n" +
                    "-X/-Y/-Z = force the negative of that axis.\n" +
                    "If the cut is horizontal when it should be vertical, try Z or Y.",
                    new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z"),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane1Radius = Config.Bind("Global Mesh Surgery settings", "Plane1Radius", 0.011f,
                new ConfigDescription(
                    "Near radius (meters) of the cylindrical/cone cut (camera side).\n" +
                    "Typical scope lens radius is ~0.01-0.02m.",
                    new AcceptableValueRange<float>(0.001f, 0.1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane1OffsetMeters = Config.Bind("Global Mesh Surgery settings", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Offset (meters) for plane 1 from linza/backLens origin along bore axis.",
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
            ReticleBaseSize = Config.Bind("Global Mesh Surgery settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Physical diameter (meters) of the reticle quad at 1x magnification.\n" +
                    "The quad is scaled by 1/magnification so screen-pixel coverage stays constant\n" +
                    "across all zoom levels.  Typical scope lens diameter is 0.02-0.04 m.\n" +
                    "Set to 0 to fall back to the legacy CylinderRadius x2 value.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ExpandSearchToWeaponRoot = Config.Bind("Global Mesh Surgery settings", "ExpandSearchToWeaponRoot", true, //Needs to be hardset
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
                    "Render a red overlay wherever the lens stencil mask is.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            StencilIncludeWeaponMeshes = Config.Bind("Global Mesh Surgery settings", "StencilIncludeWeaponMeshes", true, //Really useful ? We're only rendering the reticle on the lens mask area
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
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
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
            CustomCutRadius = Config.Bind("Per scope settings", "CutRadius", 0f,
                new ConfigDescription(
                    "Custom per-scope max cut distance in meters (0 = unlimited).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane1Radius = Config.Bind("Per scope settings", "Plane1Radius", 0.011f,
                new ConfigDescription(
                    "Custom per-scope near radius in meters.",
                    new AcceptableValueRange<float>(0.001f, 0.1f),
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
            CustomReticleBaseSize = Config.Bind("Per scope settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Custom per-scope reticle base diameter in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
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
            VignetteOpacity = Config.Bind("Scope Effects", "Vignette Opacity", 0.78f,
                new ConfigDescription(
                    "Maximum opacity of the lens vignette ring (0=invisible, 1=full black).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteSizeMult = Config.Bind("Scope Effects", "Vignette Size Multiplier", 0.41f,
                new ConfigDescription(
                    "Vignette quad diameter as a multiplier of ReticleBaseSize.\n" +
                    "1.0 = same size as reticle.  1.5 gives a visible border ring.\n" +
                    "Higher values (5-15) may be needed for high-magnification scopes.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteSoftness = Config.Bind("Scope Effects", "Vignette Softness", 1f,
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
            DebugShowScopeShadowMask = Config.Bind("Scope Effects", "DebugShowScopeShadowMask", false,
                new ConfigDescription(
                    "Render the shadow lens mask as a green overlay for debugging.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopeShadowPersistOnUnscope = Config.Bind("Scope Effects", "ScopeShadow Persist On Unscope", false,
                new ConfigDescription(
                    "Keep the scope shadow visible after leaving ADS until the FOV restore finishes, useful when FOV animation set to 0.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowOpacity = Config.Bind("Scope Effects", "ScopeShadow Opacity", 0.82f,
                new ConfigDescription(
                    "Maximum opacity of the scope shadow overlay.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowRadius = Config.Bind("Scope Effects", "ScopeShadow Radius", 0.05f,
                new ConfigDescription(
                    "Radius of the transparent centre window as a fraction of the half-screen (0.0-0.5).\n" +
                    "0.18 = window fills roughly 36% of screen width.  Increase for wider aperture.",
                    new AcceptableValueRange<float>(0.02f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowSoftness = Config.Bind("Scope Effects", "ScopeShadow Softness", 0.08f,
                new ConfigDescription(
                    "Width of the gradient edge between the clear window and the black shadow (fraction of screen).\n" +
                    "0 = hard edge.  0.05-0.1 is a natural-looking falloff.",
                    new AcceptableValueRange<float>(0f, 0.3f),
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
            DebugMeshSurgeryLifecycle = Config.Bind("Diagnostics", "DebugMeshSurgeryLifecycle", false,
                new ConfigDescription(
                    "When enabled, logs detailed scope-enter/mode-switch mesh-surgery context and last cut attempt snapshot.",
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

            LogInfo("PiP-Disabler 0.6.0 loaded.");
        }
              
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

        private static void OnModEnabledChanged(object sender, EventArgs e)
        {
            if (!ModEnabled.Value)
            {
                ScopeLifecycle.ForceExit();
                LensTransparency.FullRestoreAll();
                PiPDisabler.RestoreAllCameras();
            }
            else
            {
                ScopeLifecycle.SyncState();
            }
        }

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
            if (ScopeLifecycle.IsScoped)
            {
                ScopeLifecycle.ForceExit();
                ScopeLifecycle.SyncState();
            }
        }


        private void Update()
        {
            if (ModToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModToggleKey.Value))
            {
                ModEnabled.Value = !ModEnabled.Value;
                LogInfo($"[Global] Mod {(ModEnabled.Value ? "ENABLED" : "DISABLED")}");
            }
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

            PiPDisabler.TickBaseOpticCamera();
            ScopeLifecycle.CheckAndUpdate("Update");
            ScopeLifecycle.Tick();
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
}
